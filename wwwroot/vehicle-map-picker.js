// ════════════════════════════════════════════════════════════════════════
// 车辆地图框选器（VehicleMapPicker）
// 用途：自动化任务「归巢模式」→「选择车队」弹窗里的地图框选车辆入口。
// 背景：全量站点坐标渲染为淡色小点（地图底图，不可选）；车辆按状态着色点渲染。
// 数据流：C# 组装站点背景 + GRCS 车辆列表（含状态）与既有车队 → 传 JSON 给
// grcsVehicleMapPickerOpen → JS 侧全量渲染 Canvas，交互（框选/平移/缩放/单击/
// tooltip）本文件完成 → 确定后 resolve 选中的车名数组（JSON 字符串，取消为 null）
// → C# 写回车队选择并保存。
// 交互约定：左键拖拽=框选(替换)；Shift/Ctrl 拖拽=增选/减选；单击车辆=切换；
// 右键/中键拖拽=平移；滚轮=缩放；Esc/遮罩点击=取消。
// 数据口径：仅就绪车可选（框选命中与单击均排除）；非就绪车（充电中/执行中/报错/
// 离线）按状态着色，悬停可见全部信息；候选集外的既有车队车名在替换框选时保留
// （增量编辑不丢数据）。坐标系与站点一致（毫米），无需楼层切换。
// ════════════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    let S = null;

    /**
     * 打开车辆地图框选器。
     * @param {string} configJson 的 JSON：
     *   { Stations:[{X,Y}], Vehicles:[{Name,X,Y,IsReady,ExecutionState,UtilizationState,Power,Location}], Preselected:string[] }
     * @returns {Promise<string|null>} 确定→选中车名数组的 JSON 字符串；取消→null。
     */
    window.grcsVehicleMapPickerOpen = function (configJson) {
        return new Promise(function (resolve) {
            let config;
            try { config = JSON.parse(configJson); } catch (e) { config = null; }
            if (!config || !Array.isArray(config.Vehicles) || config.Vehicles.length === 0) {
                resolve(null);
                return;
            }
            if (S) { // 防御：上一次未关闭（理论上不会发生）
                try { S.resolve(null); } catch (e) { }
                try { S.overlay.remove(); } catch (e) { }
            }
            buildPicker(config, resolve);
        });
    };

    function makeEl(tag, cls, text) {
        const el = document.createElement(tag);
        if (cls) el.className = cls;
        if (text !== undefined && text !== null) el.textContent = text;
        return el;
    }

    function vehicleStyle(v) {
        if (!v.IsReady) {
            if (!v.IsOnline) return { color: '#64748b', name: '离线' };
            if ((v.ExecutionState || '').toUpperCase() === 'ERROR') return { color: '#f87171', name: '报错' };
            if (v.IsCharging) return { color: '#f59e0b', name: '充电中' };
            return { color: '#fbbf24', name: '执行中' };
        }
        return { color: '#4ade80', name: '就绪' };
    }

    function buildPicker(config, resolve) {
        // ── DOM 骨架（复用 smp-* 样式）──
        const overlay = makeEl('div', 'smp-overlay');
        const modal = makeEl('div', 'smp-modal');

        const toolbar = makeEl('div', 'smp-toolbar');
        toolbar.appendChild(makeEl('div', 'smp-title', '🗺️ 地图框选车辆 · 归巢车队'));
        const countEl = makeEl('span', 'smp-count', '已选 0');
        toolbar.appendChild(countEl);
        const btnClear = makeEl('button', 'btn btn-outline btn-sm', '🗑 清空');
        const btnAll = makeEl('button', 'btn btn-outline btn-sm', '☑ 全选就绪车');
        const btnCancel = makeEl('button', 'btn btn-outline btn-sm', '✕ 取消');
        const btnOk = makeEl('button', 'btn btn-primary btn-sm', '✔ 确定');
        btnClear.type = btnAll.type = btnCancel.type = btnOk.type = 'button';
        toolbar.appendChild(btnClear);
        toolbar.appendChild(btnAll);
        toolbar.appendChild(btnCancel);
        toolbar.appendChild(btnOk);
        modal.appendChild(toolbar);

        const wrap = makeEl('div', 'smp-canvas-wrap');
        const canvas = makeEl('canvas', 'smp-canvas');
        wrap.appendChild(canvas);
        const tooltip = makeEl('div', 'smp-tooltip');
        tooltip.style.display = 'none';
        wrap.appendChild(tooltip);
        modal.appendChild(wrap);

        const legend = makeEl('div', 'smp-legend');
        const legendDefs = [
            { name: '就绪', color: '#4ade80' },
            { name: '充电中', color: '#f59e0b' },
            { name: '执行中', color: '#fbbf24' },
            { name: '报错', color: '#f87171' },
            { name: '离线', color: '#64748b' }
        ];
        for (const ld of legendDefs) {
            const item = makeEl('div', 'smp-legend-item');
            const dot = makeEl('span', 'smp-legend-dot');
            dot.style.background = ld.color;
            item.appendChild(dot);
            item.appendChild(document.createTextNode(ld.name));
            legend.appendChild(item);
        }
        modal.appendChild(legend);
        modal.appendChild(makeEl('div', 'smp-hint',
            '左键拖拽=框选(替换) · Shift/Ctrl 拖拽=增选/减选 · 单击车辆=切换 · 右键/中键拖拽=平移 · 滚轮=缩放 · Esc/遮罩=取消 · 仅绿色「就绪」车可选，其余悬停可见信息'));

        overlay.appendChild(modal);
        document.body.appendChild(overlay);

        const ctx = canvas.getContext('2d');

        // ── 状态 ──
        const stations = (config.Stations || []).map(function (s) { return { X: s.X, Y: s.Y }; });
        const vehicles = config.Vehicles;
        const vehicleNames = new Set(vehicles.map(function (v) { return String(v.Name); }));
        const selection = new Set((config.Preselected || []).map(function (n) { return String(n).trim(); }).filter(Boolean));
        // 候选集外的既有车队车名（已不在 GRCS 车辆列表）：替换框选时保留，避免丢已选
        const preserved = new Set(Array.from(selection).filter(function (n) { return !vehicleNames.has(n); }));
        let view = { scale: 1, ox: 0, oy: 0, fitScale: 1 };
        let drag = null;
        let hover = null;

        function fit() {
            const w = canvas.clientWidth, h = canvas.clientHeight;
            const all = vehicles.concat(stations);
            if (all.length === 0) {
                view = { scale: 1, ox: 0, oy: 0, fitScale: 1 };
                draw();
                return;
            }
            if (w === 0 || h === 0) { requestAnimationFrame(fit); return; }
            let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
            for (const p of all) {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
            const spanX = maxX - minX, spanY = maxY - minY;
            const pad = 56;
            let scale = Math.min((w - pad * 2) / (spanX || 1), (h - pad * 2) / (spanY || 1));
            if (!isFinite(scale) || scale <= 0) scale = 1;
            if (spanX === 0 && spanY === 0) scale = Math.max(w, h) / 16;
            const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            // 地图为左下角原点、Y 向上的笛卡尔系：屏幕 Y 需翻转（sy = oy - Y*scale）
            view = { scale: scale, ox: w / 2 - cx * scale, oy: h / 2 + cy * scale, fitScale: scale };
            draw();
        }

        function draw() {
            const w = canvas.clientWidth, h = canvas.clientHeight;
            if (w === 0 || h === 0) return;
            const dpr = window.devicePixelRatio || 1;
            canvas.width = w * dpr;
            canvas.height = h * dpr;
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, w, h);

            const { scale, ox, oy } = view;
            drawGrid(w, h);

            // 背景站点：淡色小点（地图底图，不可选）
            for (const s of stations) {
                const sx = s.X * scale + ox, sy = oy - s.Y * scale;
                if (sx < -20 || sx > w + 20 || sy < -20 || sy > h + 20) continue;
                ctx.beginPath();
                ctx.arc(sx, sy, 1.6, 0, Math.PI * 2);
                ctx.fillStyle = 'rgba(100,116,139,0.55)';
                ctx.fill();
            }

            const inBox = drag && drag.mode === 'box' && drag.moved;

            // 车辆：就绪着色可选；非就绪（充电中/执行中/报错/离线）按状态着色，不可选但悬停可见信息
            for (const v of vehicles) {
                const sx = v.X * scale + ox, sy = oy - v.Y * scale;
                if (sx < -40 || sx > w + 40 || sy < -40 || sy > h + 40) continue;
                const st = vehicleStyle(v);
                if (!v.IsReady) {
                    drawVehicle(v, sx, sy, false, st.color, 5, 1, 'rgba(255,255,255,0.25)', false, false);
                    continue;
                }
                const isSel = selection.has(v.Name);
                const isHit = inBox ? rectContains(drag, sx, sy) : false;
                const isHover = hover !== null && hover.Name === v.Name;
                if (isSel)
                    drawVehicle(v, sx, sy, true, '#4ade80', 9, 3, '#ffffff', true, false);
                else if (isHit)
                    drawVehicle(v, sx, sy, true, '#4ade80', 7.5, 2, 'rgba(255,255,255,0.85)', true, false);
                else if (isHover)
                    drawVehicle(v, sx, sy, false, '#4ade80', 6, 2, '#ffffff', false, false);
                else
                    drawVehicle(v, sx, sy, false, '#4ade80', 5, 1.6, 'rgba(255,255,255,0.7)', false, false);
            }

            // 橡皮筋矩形
            if (inBox) {
                const x0 = Math.min(drag.x0, drag.x1), x1 = Math.max(drag.x0, drag.x1);
                const y0 = Math.min(drag.y0, drag.y1), y1 = Math.max(drag.y0, drag.y1);
                ctx.fillStyle = 'rgba(56,189,248,0.10)';
                ctx.fillRect(x0, y0, x1 - x0, y1 - y0);
                ctx.strokeStyle = '#38bdf8';
                ctx.lineWidth = 1;
                ctx.setLineDash([5, 4]);
                ctx.strokeRect(x0, y0, x1 - x0, y1 - y0);
                ctx.setLineDash([]);
            }
        }

        function drawVehicle(v, sx, sy, highlight, color, radius, ringWidth, ringColor, withLabel, isSel) {
            if (highlight) {
                ctx.beginPath();
                ctx.arc(sx, sy, radius + 4, 0, Math.PI * 2);
                ctx.fillStyle = 'rgba(103,232,249,0.25)';
                ctx.fill();
            }
            ctx.beginPath();
            ctx.arc(sx, sy, radius, 0, Math.PI * 2);
            ctx.fillStyle = color;
            ctx.fill();
            if (ringWidth > 0) {
                ctx.lineWidth = ringWidth;
                ctx.strokeStyle = ringColor || 'rgba(255,255,255,0.6)';
                ctx.stroke();
            }
            if (withLabel) {
                ctx.fillStyle = 'rgba(226,232,240,0.95)';
                ctx.font = '10px Consolas, monospace';
                ctx.textAlign = 'left';
                ctx.fillText(v.Name, sx + radius + 5, sy + 3);
            }
        }

        function drawGrid(w, h) {
            const { scale, ox, oy } = view;
            const step = niceStep(90 / scale);
            if (!isFinite(step) || step <= 0) return;
            ctx.strokeStyle = 'rgba(51,65,85,0.4)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            const xStart = Math.floor((-ox) / step);
            for (let i = xStart; i * step * scale + ox < w; i++) {
                const sx = Math.round(i * step * scale + ox);
                ctx.moveTo(sx, 0);
                ctx.lineTo(sx, h);
            }
            const yStart = Math.ceil((oy - h) / step);
            for (let j = yStart; j * step * scale <= oy; j++) {
                const sy = Math.round(oy - j * step * scale);
                ctx.moveTo(0, sy);
                ctx.lineTo(w, sy);
            }
            ctx.stroke();
        }

        function niceStep(rough) {
            if (!isFinite(rough) || rough <= 0) return 1;
            const pow = Math.pow(10, Math.floor(Math.log10(rough)));
            const norm = rough / pow;
            let nice;
            if (norm < 1.5) nice = 1; else if (norm < 3.5) nice = 2; else if (norm < 7.5) nice = 5; else nice = 10;
            return nice * pow;
        }

        function rectContains(d, sx, sy) {
            const x0 = Math.min(d.x0, d.x1), x1 = Math.max(d.x0, d.x1);
            const y0 = Math.min(d.y0, d.y1), y1 = Math.max(d.y0, d.y1);
            return sx >= x0 && sx <= x1 && sy >= y0 && sy <= y1;
        }

        function hitTest(px, py) {
            const { scale, ox, oy } = view;
            let best = null, bestD = Infinity;
            for (const v of vehicles) {
                const sx = v.X * scale + ox, sy = oy - v.Y * scale;
                const d = Math.hypot(sx - px, sy - py);
                if (d < bestD) { bestD = d; best = v; }
            }
            return bestD <= 10 ? best : null;
        }

        // ── 选中操作 ──
        function toggleName(n) {
            if (selection.has(n)) selection.delete(n); else selection.add(n);
        }

        function applyBox(x0, y0, x1, y1, additive) {
            const ax = Math.min(x0, x1), bx = Math.max(x0, x1);
            const ay = Math.min(y0, y1), by = Math.max(y0, y1);
            const { scale, ox, oy } = view;
            const hits = vehicles.filter(function (v) {
                if (!v.IsReady) return false;
                const sx = v.X * scale + ox, sy = oy - v.Y * scale;
                return sx >= ax && sx <= bx && sy >= ay && sy <= by;
            }).map(function (v) { return v.Name; });
            if (additive) {
                for (const n of hits) toggleName(n);
            } else {
                selection.clear();
                for (const n of hits) selection.add(n);
                // 保留候选集外的既有车队车名（增量编辑不丢数据）
                for (const n of preserved) selection.add(n);
            }
        }

        function updateCount() {
            countEl.textContent = '已选 ' + selection.size;
        }

        // ── Tooltip ──
        function showTooltip(v, px, py) {
            tooltip.textContent = '';
            const st = vehicleStyle(v);
            tooltip.appendChild(makeEl('div', 'smp-tt-mark', '🚚 ' + v.Name + ' · ' + st.name));
            const detail = '位置 ' + (v.Location || '—') +
                ' ｜ 电量 ' + Math.round((v.Power || 0) * 100) + '%' +
                (v.IsReady ? '（可勾选）' : '（不可选：非就绪）');
            tooltip.appendChild(makeEl('div', 'smp-tt-type', detail));
            tooltip.style.display = 'block';
            const pad = 12;
            const tw = tooltip.offsetWidth, th = tooltip.offsetHeight;
            let tx = px + 12, ty = py - th - 10;
            if (tx + tw > wrap.clientWidth - pad) tx = px - tw - 12;
            if (ty < pad) ty = py + 14;
            tooltip.style.left = tx + 'px';
            tooltip.style.top = ty + 'px';
        }

        function hideTooltip() { tooltip.style.display = 'none'; }

        // ── 指针交互 ──
        function onDown(e) {
            if (e.button === 1 || e.button === 2) {
                const rect = canvas.getBoundingClientRect();
                drag = { mode: 'pan', x0: e.clientX - rect.left, y0: e.clientY - rect.top };
                canvas.classList.add('grabbing');
            } else {
                const rect = canvas.getBoundingClientRect();
                const px = e.clientX - rect.left, py = e.clientY - rect.top;
                const hit = hitTest(px, py);
                drag = {
                    mode: 'box', x0: px, y0: py, x1: px, y1: py, moved: false,
                    additive: !!(e.shiftKey || e.ctrlKey),
                    clickName: hit ? hit.Name : null
                };
            }
            try { canvas.setPointerCapture(e.pointerId); } catch (err) { }
        }

        function onMove(e) {
            const rect = canvas.getBoundingClientRect();
            const px = e.clientX - rect.left, py = e.clientY - rect.top;
            if (drag) {
                if (drag.mode === 'pan') {
                    view.ox += px - drag.x0;
                    view.oy += py - drag.y0;
                    drag.x0 = px; drag.y0 = py;
                    draw();
                } else {
                    drag.x1 = px; drag.y1 = py;
                    if (Math.hypot(px - drag.x0, py - drag.y0) > 4) drag.moved = true;
                    if (drag.moved) { hover = null; hideTooltip(); draw(); }
                }
                return;
            }
            const hit = hitTest(px, py);
            const prevName = hover ? hover.Name : null;
            const curName = hit ? hit.Name : null;
            if (curName !== prevName) { hover = hit; draw(); }
            if (hit) showTooltip(hit, px, py); else hideTooltip();
        }

        function onUp(e) {
            if (!drag) return;
            const rect = canvas.getBoundingClientRect();
            const px = e.clientX - rect.left, py = e.clientY - rect.top;
            const d = drag;
            drag = null;
            canvas.classList.remove('grabbing');
            if (d.mode === 'pan') { draw(); return; }
            if (!d.moved) {
                if (d.clickName) {
                    const hit = vehicles.find(function (x) { return x.Name === d.clickName; });
                    if (hit && hit.IsReady) toggleName(d.clickName); // 单击车辆 = 切换选中（仅就绪车）
                }
            } else {
                applyBox(d.x0, d.y0, px, py, d.additive);
            }
            draw();
            updateCount();
        }

        function onWheel(e) {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const mx = e.clientX - rect.left, my = e.clientY - rect.top;
            const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
            const scale = view.scale;
            const ns = Math.min(Math.max(scale * factor, view.fitScale / 20), view.fitScale * 2000);
            const wx = (mx - view.ox) / scale, wy = (view.oy - my) / scale;
            view = { scale: ns, ox: mx - wx * ns, oy: my + wy * ns, fitScale: view.fitScale };
            draw();
        }

        // ── 按钮 / 关闭 ──
        function closePicker(result) {
            if (!S) return;
            window.removeEventListener('keydown', onKey);
            if (S.ro) S.ro.disconnect();
            const resolveFn = S.resolve;
            S = null;
            overlay.remove();
            resolveFn(result === null ? null : JSON.stringify(result));
        }

        function onKey(e) {
            if (e.key === 'Escape') { e.preventDefault(); closePicker(null); }
        }

        btnClear.addEventListener('click', function () {
            selection.clear();
            preserved.clear(); // 清空 = 全部清掉（含候选集外的车队车名）
            updateCount();
            draw();
        });
        btnAll.addEventListener('click', function () {
            for (const v of vehicles) if (v.IsReady) selection.add(v.Name);
            updateCount();
            draw();
        });
        btnCancel.addEventListener('click', function () { closePicker(null); });
        btnOk.addEventListener('click', function () { closePicker(Array.from(selection)); });
        overlay.addEventListener('mousedown', function (e) { if (e.target === overlay) closePicker(null); });
        canvas.addEventListener('contextmenu', function (e) { e.preventDefault(); });
        canvas.addEventListener('pointerdown', onDown);
        canvas.addEventListener('pointermove', onMove);
        canvas.addEventListener('pointerup', onUp);
        canvas.addEventListener('pointercancel', onUp);
        canvas.addEventListener('pointerleave', function () {
            if (!drag) hideTooltip();
        });
        canvas.addEventListener('wheel', onWheel, { passive: false });
        window.addEventListener('keydown', onKey);

        const ro = new ResizeObserver(function () { requestAnimationFrame(fit); });
        ro.observe(wrap);

        S = { resolve: resolve, overlay: overlay, ro: ro };

        requestAnimationFrame(fit);
        updateCount();
    }
})();