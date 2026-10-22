/* Moewshare Web Runtime 0.1 — Babylon.js. C#-скрипты здесь НЕ исполняются. */
(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  function fail(text) {
    const m = $('msg'); if (m) { m.style.display = 'block'; m.textContent = 'Ошибка: ' + text; }
    console.error(text);
  }
  window.addEventListener('error', e => fail(e.message));
  window.addEventListener('unhandledrejection', e => fail((e.reason && e.reason.message) || String(e.reason)));

  const loadScript = src => new Promise((res, rej) => {
    const s = document.createElement('script'); s.src = src; s.onload = res;
    s.onerror = () => rej(new Error('не удалось загрузить ' + src)); document.head.appendChild(s);
  });
  async function loadBabylon(url) {
    if (window.BABYLON) return;
    try { await loadScript('lib/babylon.js'); }
    catch { if (!url) throw new Error('Babylon.js не найден (положите lib/babylon.js или задайте babylonUrl).'); await loadScript(url); }
  }
  async function getJson(p) {
    const r = await fetch(p, { cache: 'no-store' });
    if (!r.ok) throw new Error(p + ': HTTP ' + r.status);
    return r.json();
  }

  async function start(opts) {
    const project = await getJson(opts.project);
    const scene = await getJson(project.startScene);
    await loadBabylon(project.babylonUrl);
    const B = BABYLON, canvas = $(opts.canvas);
    const engine = new B.Engine(canvas, true), sc = new B.Scene(engine);
    const hex = c => (c && /^#[0-9a-fA-F]{6}$/.test(c)) ? c : '#cccccc';
    sc.clearColor = B.Color4.FromHexString(hex(scene.background) + 'ff');
    const amb = new B.HemisphericLight('ambient', new B.Vector3(0, 1, 0), sc);
    amb.intensity = 0.95;
    amb.diffuse = new B.Color3(1, 1, 1);
    amb.groundColor = new B.Color3(0.85, 0.85, 0.85);

    const solids = [], spinners = [], triggers = []; let player = null;
    const meshMap = {};
    const rad = d => d * Math.PI / 180;
    for (const o of scene.objects) {
      if (['ui_canvas', 'ui_text', 'ui_image', 'ui_button'].includes(o.type)) {
        continue;
      }
      const p = o.position, s = o.scale, r = o.rotation;
      if (o.type === 'light') {
        const l = new B.PointLight(o.name, new B.Vector3(p.x, p.y, p.z), sc);
        const colHex = (o.color && /^#[0-9a-fA-F]{6}$/.test(o.color)) ? o.color : '#ffffff';
        l.diffuse = B.Color3.FromHexString(colHex);
        l.specular = new B.Color3(0.2, 0.2, 0.2);
        l.intensity = 0.7;
        l.range = 30;
        continue;
      }
      let m;
      if (o.type === 'sphere') {
        m = B.MeshBuilder.CreateSphere(o.name, { diameter: 1 }, sc);
      } else if (o.type === 'cylinder') {
        m = B.MeshBuilder.CreateCylinder(o.name, { diameter: 1, height: 1 }, sc);
      } else if (o.type === 'plane' || o.type === 'sprite') {
        m = B.MeshBuilder.CreatePlane(o.name, { size: 1, sideOrientation: B.Mesh.DOUBLESIDE }, sc);
        if (o.type === 'sprite' || o.billboard) {
          m.billboardMode = B.Mesh.BILLBOARDMODE_ALL;
        }
      } else {
        m = B.MeshBuilder.CreateBox(o.name, { size: 1 }, sc);
      }
      m.position.set(p.x, p.y, p.z); m.rotation.set(rad(r.x), rad(r.y), rad(r.z)); m.scaling.set(s.x, s.y, s.z);
      if (o.isVisible === false) {
        m.setEnabled(false);
      }
      meshMap[o.id] = m;
      meshMap[o.name] = m;

      const mat = new B.StandardMaterial(o.name + '_mat', sc);
      if (o.texture) {
        mat.diffuseColor = new B.Color3(1, 1, 1);
        mat.specularColor = new B.Color3(0, 0, 0);
        mat.emissiveColor = new B.Color3(0.2, 0.2, 0.2);
        try {
          const tex = new B.Texture(o.texture, sc, false, false);
          tex.hasAlpha = true;
          mat.diffuseTexture = tex;
          mat.useAlphaFromDiffuseTexture = true;
          mat.transparencyMode = B.Material.MATERIAL_ALPHATESTANDBLEND;
          mat.backFaceCulling = false;
        } catch (texErr) {
          console.warn('Не удалось загрузить текстуру ' + o.texture, texErr);
        }
      } else {
        mat.diffuseColor = B.Color3.FromHexString(hex(o.color));
        mat.specularColor = new B.Color3(0.1, 0.1, 0.1);
      }
      m.material = mat;
      const rec = { o, m, h: { x: Math.abs(s.x) / 2, y: Math.abs(s.y) / 2, z: Math.abs(s.z) / 2 }, vy: 0, grounded: false, start: m.position.clone() };
      if (o.solid && !o.isTrigger && o.isVisible !== false) solids.push(rec);
      if (o.isTrigger && o.isVisible !== false) triggers.push(rec);
      if (o.behavior === 'spin' || (o.components && o.components.includes('Spin'))) {
        spinners.push({ m, sp: (o.spinSpeed || 90) * Math.PI / 180 });
      }
      if (!player && (['platformer', 'topdown', 'thirdperson'].includes(o.behavior) || (o.components && (o.components.includes('PlatformerController') || o.components.includes('TopDownController'))))) {
        if (!o.behavior || o.behavior === 'none') {
          if (o.components && o.components.includes('TopDownController')) o.behavior = 'topdown';
          else o.behavior = 'platformer';
        }
        player = rec;
      }
    }

    // Привязка объектов (Parenting как в Unity)
    for (const o of scene.objects) {
      if (o.parentId && meshMap[o.parentId] && meshMap[o.id]) {
        meshMap[o.id].parent = meshMap[o.parentId];
      }
    }

    const cam = scene.camera || { mode: 'follow3d', zoom: 14 };
    const zoom = (cam.zoom && cam.zoom > 0) ? cam.zoom : 14;
    let camera;

    // Pitch: 0 = front 2D, 45 = isometric, 90 = straight top-down
    let pitchDeg = cam.pitch;
    if (pitchDeg === undefined || pitchDeg === null) {
      if (cam.mode === 'topdown') pitchDeg = 90;
      else if (cam.mode === 'isometric') pitchDeg = 45;
      else if (cam.mode === 'side2d') pitchDeg = 0;
      else pitchDeg = 35;
    }
    const pitchRad = rad(pitchDeg);
    const yawDeg = cam.yaw || 0;
    const yawRad = rad(yawDeg);
    const offY = cam.offsetY || 0;
    const offZ = cam.offsetZ || 0;

    if (cam.mode === 'follow3d') {
      const alpha = -Math.PI / 2 + yawRad;
      const beta = Math.max(0.05, Math.min(Math.PI - 0.05, rad(90 - pitchDeg)));
      camera = new B.ArcRotateCamera('cam', alpha, beta, zoom, new B.Vector3(0, offY, offZ), sc);
      camera.attachControl(canvas, true);
    } else {
      camera = new B.FreeCamera('cam', new B.Vector3(0, 0, 0), sc);
      camera.mode = B.Camera.ORTHOGRAPHIC_CAMERA;

      const dist = 40;
      if (pitchDeg >= 89.5) {
        // Straight top-down (looking along -Y)
        camera.position.set(0, dist + offY, offZ);
        camera.setTarget(new B.Vector3(0, offY, offZ));
        camera.upVector = new B.Vector3(0, 0, 1); // Fix gimbal lock / singularity with default (0,1,0)
      } else if (pitchDeg <= 0.5) {
        // Straight 2D side view (looking along +Z)
        camera.position.set(0, 1 + offY, -dist + offZ);
        camera.setTarget(new B.Vector3(0, 1 + offY, offZ));
        camera.upVector = new B.Vector3(0, 1, 0);
      } else {
        // Isometric / angled view
        const cy = dist * Math.sin(pitchRad);
        const cz = -dist * Math.cos(pitchRad) * Math.cos(yawRad);
        const cx = dist * Math.cos(pitchRad) * Math.sin(yawRad);
        camera.position.set(cx, cy + offY, cz + offZ);
        camera.setTarget(new B.Vector3(0, offY, offZ));
        camera.upVector = new B.Vector3(0, 1, 0);
      }
    }

    const fit = () => {
      if (cam.mode === 'follow3d') return;
      const ar = engine.getRenderWidth() / Math.max(1, engine.getRenderHeight()), h = zoom / 2;
      camera.orthoTop = h; camera.orthoBottom = -h; camera.orthoLeft = -h * ar; camera.orthoRight = h * ar;
    };

    const keys = {};
    const block = ['Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'];
    addEventListener('keydown', e => {
      keys[e.code] = true;
      if (typeof triggerInputEvent === 'function') triggerInputEvent(e.code);
      if (block.includes(e.code)) e.preventDefault();
    });
    addEventListener('keyup', e => { keys[e.code] = false; });

    const overlap = (a, b) => Math.abs(a.m.position.x - b.m.position.x) < a.h.x + b.h.x &&
      Math.abs(a.m.position.y - b.m.position.y) < a.h.y + b.h.y && Math.abs(a.m.position.z - b.m.position.z) < a.h.z + b.h.z;
    function move(p, axis, d) {
      if (!d) return false;
      p.m.position[axis] += d;
      for (const s of solids) {
        if (s === p || !overlap(p, s)) continue;
        p.m.position[axis] = s.m.position[axis] + (d > 0 ? -1 : 1) * (s.h[axis] + p.h[axis]);
        return true;
      }
      return false;
    }
    function step(p, dt) {
      const b = p.o.behavior;
      const sp = p.o.speed || (b === 'topdown' ? 6 : 8);
      const jf = p.o.jumpForce || 10;
      const gr = p.o.gravity || 25;
      let ax = 0, az = 0;
      if (keys.ArrowLeft || keys.KeyA) ax -= 1;
      if (keys.ArrowRight || keys.KeyD) ax += 1;
      if (keys.ArrowUp || keys.KeyW) az += 1;
      if (keys.ArrowDown || keys.KeyS) az -= 1;

      if (b === 'topdown') {
        // Fix Top-Down movement: in 2D (Side2D or pitch < 30) W/S moves Y; in 3D/TopDown (pitch >= 30) W/S moves Z
        const is2dScreen = (cam.movePlane === 'xy') || (cam.movePlane !== 'xz' && (cam.mode === 'side2d' || pitchDeg < 30));
        if (is2dScreen) {
          move(p, 'x', ax * sp * dt);
          move(p, 'y', az * sp * dt);
        } else {
          move(p, 'x', ax * sp * dt);
          move(p, 'z', az * sp * dt);
        }
      } else {
        // Platformer or thirdperson
        move(p, 'x', ax * sp * dt);
        if (b !== 'platformer') move(p, 'z', az * sp * dt);
        p.vy -= gr * dt;
        if (p.grounded && (keys.Space || (b === 'platformer' && (keys.ArrowUp || keys.KeyW)))) {
          p.vy = jf;
          p.grounded = false;
        }
        p.grounded = false;
        if (move(p, 'y', p.vy * dt)) {
          if (p.vy < 0) p.grounded = true;
          p.vy = 0;
        }
        if (p.m.position.y < -50) {
          p.m.position.copyFrom(p.start);
          p.vy = 0;
        }
      }
    }

    // Setup UI Canvas Screen Overlay
    const canvasObj = scene.objects.find(o => o.type === 'ui_canvas');
    const isCanvasVisible = canvasObj ? (canvasObj.isVisible !== false) : true;
    const uiElements = isCanvasVisible ? scene.objects.filter(o => ['ui_text', 'ui_image', 'ui_button'].includes(o.type) && o.isVisible !== false) : [];
    const uiDomMap = {};
    if (uiElements.length > 0) {
      let uiOverlay = $('moewshare-ui-layer');
      if (!uiOverlay) {
        uiOverlay = document.createElement('div');
        uiOverlay.id = 'moewshare-ui-layer';
        uiOverlay.style.cssText = 'position:fixed;inset:0;pointer-events:none;overflow:hidden;user-select:none;-webkit-user-select:none;font-family:system-ui,sans-serif;z-index:10;';
        document.body.appendChild(uiOverlay);
      }
      uiOverlay.innerHTML = '';

      function triggerAction(action) {
        if (!action) return;
        if (action === 'jump') {
          if (player) { player.vy = 9; player.grounded = false; }
        } else if (action === 'restart') {
          if (player) { player.m.position.copyFrom(player.start); player.vy = 0; }
        } else if (action === 'alert') {
          alert('Moewshare: кнопка нажата!');
        }
      }

      for (const el of uiElements) {
        if (el.isVisible === false) continue;
        const p = el.position || { x: 0, y: 0 };
        const s = el.scale || { x: 100, y: 40 };
        const node = document.createElement(el.type === 'ui_button' ? 'button' : (el.type === 'ui_image' ? 'img' : 'div'));
        node.style.position = 'absolute';
        node.style.left = `calc(50% + ${p.x}px)`;
        node.style.top = `calc(50% - ${p.y}px)`;
        node.style.transform = 'translate(-50%, -50%)';

        if (el.type === 'ui_text') {
          node.textContent = el.uiText || el.name || 'Text';
          node.style.fontSize = (el.uiFontSize || 20) + 'px';
          node.style.color = hex(el.color);
          node.style.fontWeight = 'bold';
          node.style.textShadow = '0 1px 3px rgba(0,0,0,0.3)';
        } else if (el.type === 'ui_image') {
          node.style.width = Math.abs(s.x) + 'px';
          node.style.height = Math.abs(s.y) + 'px';
          if (el.texture) node.src = el.texture;
          else node.style.backgroundColor = hex(el.color);
          node.style.borderRadius = '8px';
          node.style.objectFit = 'contain';
        } else if (el.type === 'ui_button') {
          node.textContent = el.uiText || 'Кнопка';
          node.style.pointerEvents = 'auto';
          node.style.width = Math.abs(s.x) + 'px';
          node.style.height = Math.abs(s.y) + 'px';
          node.style.backgroundColor = hex(el.color);
          node.style.color = '#4A2929';
          node.style.fontSize = (el.uiFontSize || 16) + 'px';
          node.style.fontWeight = 'bold';
          node.style.border = '2px solid rgba(255,255,255,0.7)';
          node.style.borderRadius = '16px';
          node.style.boxShadow = '0 4px 14px rgba(0,0,0,0.2)';
          node.style.cursor = 'pointer';
          node.style.transition = 'transform 0.08s ease, filter 0.08s ease';

          const press = () => { node.style.transform = 'translate(-50%, -50%) scale(0.92)'; node.style.filter = 'brightness(0.9)'; };
          const release = () => { node.style.transform = 'translate(-50%, -50%) scale(1)'; node.style.filter = 'brightness(1)'; };

          node.addEventListener('mousedown', press);
          node.addEventListener('mouseup', release);
          node.addEventListener('mouseleave', release);
          let lastTouchTime = 0;
          node.addEventListener('touchstart', (e) => {
            e.preventDefault();
            lastTouchTime = Date.now();
            press();
            if (typeof triggerEvent === 'function') {
              triggerEvent('event_ui_button_clicked', el, null);
            }
            triggerAction(el.uiAction);
          }, { passive: false });
          node.addEventListener('touchend', (e) => { e.preventDefault(); release(); }, { passive: false });
          node.addEventListener('click', () => {
            if (Date.now() - lastTouchTime < 400) return;
            if (typeof triggerEvent === 'function') {
              triggerEvent('event_ui_button_clicked', el, null);
            }
            triggerAction(el.uiAction);
          });
        }

        uiDomMap[el.id] = node;
        uiDomMap[el.name] = node;
        uiOverlay.appendChild(node);
      }
    }

    // ==========================================
    // Moewshare Event System JavaScript Runtime
    // ==========================================
    let audioCtx = null;
    function playBeep(type) {
      try {
        if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        if (audioCtx.state === 'suspended') audioCtx.resume();
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        const now = audioCtx.currentTime;
        if (type === 'coin') {
          osc.type = 'sine';
          osc.frequency.setValueAtTime(987.77, now);
          osc.frequency.setValueAtTime(1318.51, now + 0.08);
          gain.gain.setValueAtTime(0.18, now);
          gain.gain.exponentialRampToValueAtTime(0.001, now + 0.35);
          osc.start(now);
          osc.stop(now + 0.35);
        } else if (type === 'damage') {
          osc.type = 'sawtooth';
          osc.frequency.setValueAtTime(220, now);
          osc.frequency.linearRampToValueAtTime(110, now + 0.18);
          gain.gain.setValueAtTime(0.2, now);
          gain.gain.exponentialRampToValueAtTime(0.001, now + 0.22);
          osc.start(now);
          osc.stop(now + 0.22);
        } else if (type === 'door') {
          osc.type = 'triangle';
          osc.frequency.setValueAtTime(440, now);
          osc.frequency.linearRampToValueAtTime(659.25, now + 0.25);
          gain.gain.setValueAtTime(0.18, now);
          gain.gain.exponentialRampToValueAtTime(0.001, now + 0.3);
          osc.start(now);
          osc.stop(now + 0.3);
        } else if (type === 'win') {
          osc.type = 'triangle';
          osc.frequency.setValueAtTime(523.25, now);
          osc.frequency.setValueAtTime(659.25, now + 0.1);
          osc.frequency.setValueAtTime(783.99, now + 0.2);
          osc.frequency.setValueAtTime(1046.50, now + 0.3);
          gain.gain.setValueAtTime(0.22, now);
          gain.gain.exponentialRampToValueAtTime(0.001, now + 0.6);
          osc.start(now);
          osc.stop(now + 0.6);
        } else {
          osc.type = 'sine';
          osc.frequency.setValueAtTime(587.33, now);
          gain.gain.setValueAtTime(0.15, now);
          gain.gain.exponentialRampToValueAtTime(0.001, now + 0.2);
          osc.start(now);
          osc.stop(now + 0.2);
        }
      } catch (e) {
        console.warn('Audio synthesis unavailable:', e);
      }
    }

    const eventGraph = scene.eventGraph;
    let activeGraph = eventGraph;
    const graphVars = {};
    const inventory = new Set();
    const healthMap = {};
    const doOnceSet = new Set();
    let currentSelf = null, currentOther = null, currentDt = 0;
    let stepCount = 0;

    if (eventGraph && eventGraph.variables) {
      for (const v of eventGraph.variables) {
        let val = v.defaultValue;
        const vt = String(v.type).toLowerCase();
        if (vt === '1' || vt === 'boolean') val = (val === 'true' || val === true);
        else if (vt === '2' || vt === 'integer') val = parseInt(val, 10) || 0;
        else if (vt === '3' || vt === 'float') val = parseFloat(val) || 0.0;
        graphVars[v.name] = val;
      }
    }

    for (const o of scene.objects) {
      if (o.eventGraph && o.eventGraph.variables) {
        for (const v of o.eventGraph.variables) {
          if (graphVars[v.name] === undefined) {
            let val = v.defaultValue;
            const vt = String(v.type).toLowerCase();
            if (vt === '1' || vt === 'boolean') val = (val === 'true' || val === true);
            else if (vt === '2' || vt === 'integer') val = parseInt(val, 10) || 0;
            else if (vt === '3' || vt === 'float') val = parseFloat(val) || 0.0;
            graphVars[v.name] = val;
          }
        }
      }
    }

    for (const o of scene.objects) {
      healthMap[o.id] = 100;
      healthMap[o.name] = 100;
    }

    function getConnectedTargets(fromNodeId, fromPortId) {
      if (!activeGraph || !activeGraph.connections) return [];
      return activeGraph.connections.filter(c => c.fromNodeId === fromNodeId && c.fromPortId === fromPortId);
    }

    function getConnectedSource(toNodeId, toPortId) {
      if (!activeGraph || !activeGraph.connections) return null;
      return activeGraph.connections.find(c => c.toNodeId === toNodeId && c.toPortId === toPortId);
    }

    function findGraphNode(id) {
      if (!activeGraph || !activeGraph.nodes) return null;
      return activeGraph.nodes.find(n => n.id === id);
    }

    function evaluateValue(node, portId) {
      const conn = getConnectedSource(node.id, portId);
      if (!conn) {
        const inp = (node.inputs || []).find(p => p.id === portId);
        return inp ? (inp.defaultValue ?? '') : '';
      }
      const srcNode = findGraphNode(conn.fromNodeId);
      if (!srcNode) return '';
      return evaluateOutput(srcNode, conn.fromPortId);
    }

    function evaluateDouble(node, portId) {
      const v = evaluateValue(node, portId);
      if (typeof v === 'number') return v;
      const num = parseFloat(v);
      return isNaN(num) ? 0.0 : num;
    }

    function evaluateInt(node, portId) {
      const v = evaluateValue(node, portId);
      if (typeof v === 'number') return Math.floor(v);
      const num = parseInt(v, 10);
      return isNaN(num) ? 0 : num;
    }

    function evaluateBool(node, portId) {
      const v = evaluateValue(node, portId);
      if (typeof v === 'boolean') return v;
      if (typeof v === 'number') return Math.abs(v) > 0.0001;
      return v === 'true' || v === '1';
    }

    function evaluateString(node, portId) {
      const v = evaluateValue(node, portId);
      return v === null || v === undefined ? '' : String(v);
    }

    function evaluateOutput(node, portId) {
      const props = node.properties || {};
      switch (node.nodeType) {
        case 'event_trigger_enter':
          if (portId === 'out_other') return currentOther || { name: 'Player' };
          if (portId === 'out_other_name') return currentOther ? currentOther.name : 'Player';
          if (portId === 'out_self') return currentSelf || { name: 'Self' };
          return '';
        case 'event_ui_button_clicked':
          if (portId === 'out_name') return currentSelf ? currentSelf.name : '';
          return '';
        case 'event_object_clicked':
          if (portId === 'out_self') return currentSelf || { name: 'Self' };
          if (portId === 'out_name') return currentSelf ? currentSelf.name : '';
          return '';
        case 'event_update':
          if (portId === 'out_dt') return currentDt;
          return 0;
        case 'var_get':
          return graphVars[props.VariableName] ?? 0;
        case 'var_set':
          return graphVars[props.VariableName] ?? evaluateValue(node, 'in_val');
        case 'var_increment':
        case 'var_decrement':
          return graphVars[props.VariableName] ?? 0;
        case 'math_add':
          return evaluateDouble(node, 'in_a') + evaluateDouble(node, 'in_b');
        case 'math_subtract':
          return evaluateDouble(node, 'in_a') - evaluateDouble(node, 'in_b');
        case 'math_multiply':
          return evaluateDouble(node, 'in_a') * evaluateDouble(node, 'in_b');
        case 'math_divide': {
          const b = evaluateDouble(node, 'in_b');
          return Math.abs(b) > 0.00001 ? evaluateDouble(node, 'in_a') / b : 0;
        }
        case 'math_clamp': {
          const val = evaluateDouble(node, 'in_val');
          const min = evaluateDouble(node, 'in_min');
          const max = evaluateDouble(node, 'in_max');
          return Math.max(min, Math.min(max, val));
        }
        case 'math_random_int': {
          const min = evaluateInt(node, 'in_min');
          const max = evaluateInt(node, 'in_max');
          return Math.floor(Math.random() * (max - min + 1)) + min;
        }
        case 'logic_equal': {
          const a = evaluateValue(node, 'in_a');
          const b = evaluateValue(node, 'in_b');
          return String(a) === String(b);
        }
        case 'logic_not_equal': {
          const a = evaluateValue(node, 'in_a');
          const b = evaluateValue(node, 'in_b');
          return String(a) !== String(b);
        }
        case 'logic_greater':
          return evaluateDouble(node, 'in_a') > evaluateDouble(node, 'in_b');
        case 'logic_greater_equal':
          return evaluateDouble(node, 'in_a') >= evaluateDouble(node, 'in_b');
        case 'logic_less':
          return evaluateDouble(node, 'in_a') < evaluateDouble(node, 'in_b');
        case 'logic_less_equal':
          return evaluateDouble(node, 'in_a') <= evaluateDouble(node, 'in_b');
        case 'logic_and':
          return evaluateBool(node, 'in_a') && evaluateBool(node, 'in_b');
        case 'logic_or':
          return evaluateBool(node, 'in_a') || evaluateBool(node, 'in_b');
        case 'logic_not':
          return !evaluateBool(node, 'in_a');
        case 'object_get_player':
          return player ? player.o : { name: 'Player' };
        case 'object_get_self':
          return currentSelf || { name: 'Self' };
        case 'object_get_by_name':
          return scene.objects.find(o => o.name === props.ObjectName) || { name: props.ObjectName || '' };
        case 'ui_format_text': {
          const pre = evaluateString(node, 'in_prefix');
          const val = evaluateString(node, 'in_val');
          const suf = evaluateString(node, 'in_suffix');
          return pre + val + suf;
        }
        case 'inventory_has_item':
          return inventory.has(props.ItemId || 'Key');
        case 'flow_for_loop':
          return parseInt(props._current_index, 10) || 0;
        case 'health_damage':
          if (portId === 'out_hp') return parseInt(props._current_hp, 10) || 0;
          if (portId === 'out_dead') return props._is_dead === 'true';
          return 0;
        default:
          return 0;
      }
    }

    function executeDownstream(node, portId) {
      const conns = getConnectedTargets(node.id, portId);
      for (const c of conns) {
        const next = findGraphNode(c.toNodeId);
        if (next) executeNode(next, c.toPortId);
      }
    }

    function executeNode(node, inPortId) {
      if (++stepCount > 5000) {
        console.warn('Лимит выполнения событий Moewshare превышен (5000 шагов)');
        return;
      }
      const props = node.properties || {};
      switch (node.nodeType) {
        case 'flow_branch': {
          const cond = evaluateBool(node, 'in_cond');
          executeDownstream(node, cond ? 'out_true' : 'out_false');
          break;
        }
        case 'flow_sequence': {
          executeDownstream(node, 'out_0');
          executeDownstream(node, 'out_1');
          executeDownstream(node, 'out_2');
          break;
        }
        case 'flow_for_loop': {
          const first = evaluateInt(node, 'in_first');
          const last = evaluateInt(node, 'in_last');
          for (let i = first; i <= last; i++) {
            if (stepCount > 5000) break;
            props._current_index = String(i);
            executeDownstream(node, 'out_body');
          }
          executeDownstream(node, 'out_done');
          break;
        }
        case 'flow_do_once': {
          if (inPortId === 'in_reset') {
            doOnceSet.delete(node.id);
          } else {
            if (!doOnceSet.has(node.id)) {
              doOnceSet.add(node.id);
              executeDownstream(node, 'out_exec');
            }
          }
          break;
        }
        case 'flow_delay': {
          const durSec = evaluateDouble(node, 'in_duration') || 1;
          setTimeout(() => executeDownstream(node, 'out_exec'), Math.max(10, durSec * 1000));
          break;
        }
        case 'var_set': {
          if (props.VariableName) {
            graphVars[props.VariableName] = evaluateValue(node, 'in_val');
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'var_increment': {
          if (props.VariableName) {
            const cur = parseInt(graphVars[props.VariableName], 10) || 0;
            graphVars[props.VariableName] = cur + 1;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'var_decrement': {
          if (props.VariableName) {
            const cur = parseInt(graphVars[props.VariableName], 10) || 0;
            graphVars[props.VariableName] = cur - 1;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'object_destroy': {
          let target = evaluateValue(node, 'in_target') || currentSelf || currentOther;
          if (typeof target === 'string') {
            target = scene.objects.find(o => o.id === target || o.name === target);
          }
          if (target && target.id) {
            const m = meshMap[target.id] || meshMap[target.name];
            if (m) {
              m.setEnabled(false);
              m.visibility = 0;
            }
            target.isVisible = false;
            // remove from triggers & solids
            const trIdx = triggers.findIndex(t => t.o.id === target.id);
            if (trIdx >= 0) triggers.splice(trIdx, 1);
            const sIdx = solids.findIndex(s => s.o.id === target.id);
            if (sIdx >= 0) solids.splice(sIdx, 1);
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'object_set_active': {
          let target = evaluateValue(node, 'in_target') || currentSelf;
          if (typeof target === 'string') {
            target = scene.objects.find(o => o.id === target || o.name === target);
          }
          const active = evaluateBool(node, 'in_active');
          if (target && target.id) {
            const m = meshMap[target.id] || meshMap[target.name];
            if (m) {
              m.setEnabled(active);
              m.visibility = active ? 1 : 0;
            }
            target.isVisible = active;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'object_set_position': {
          let target = evaluateValue(node, 'in_target') || currentSelf;
          if (typeof target === 'string') {
            target = scene.objects.find(o => o.id === target || o.name === target);
          }
          if (target && target.id) {
            const m = meshMap[target.id] || meshMap[target.name];
            if (m) {
              const x = evaluateDouble(node, 'in_x');
              const y = evaluateDouble(node, 'in_y');
              const z = evaluateDouble(node, 'in_z');
              m.position.set(x, y, z);
            }
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'health_damage': {
          let target = evaluateValue(node, 'in_target') || currentOther || currentSelf;
          if (typeof target === 'string') {
            target = scene.objects.find(o => o.id === target || o.name === target);
          }
          const dmg = evaluateInt(node, 'in_dmg');
          if (target && target.id) {
            let hp = healthMap[target.id] ?? 100;
            hp = Math.max(0, hp - dmg);
            healthMap[target.id] = hp;
            healthMap[target.name] = hp;
            props._current_hp = String(hp);
            props._is_dead = String(hp <= 0);
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'health_heal': {
          let target = evaluateValue(node, 'in_target') || currentSelf;
          if (typeof target === 'string') {
            target = scene.objects.find(o => o.id === target || o.name === target);
          }
          const heal = evaluateInt(node, 'in_amount');
          if (target && target.id) {
            let hp = healthMap[target.id] ?? 100;
            hp = Math.min(100, hp + heal);
            healthMap[target.id] = hp;
            healthMap[target.name] = hp;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'ui_set_text': {
          const targetName = props.TargetName || props.TargetUI || '';
          const prefix = props.Prefix || '';
          const suffix = props.Suffix || '';
          const rawVal = evaluateString(node, 'in_text');
          const newText = prefix + rawVal + suffix;

          let domEl = null;
          if (targetName && targetName !== 'Text') {
            domEl = uiDomMap[targetName];
            if (!domEl) {
              const textObj = scene.objects.find(o => o.name === targetName || o.id === targetName);
              if (textObj) domEl = uiDomMap[textObj.id] || uiDomMap[textObj.name];
            }
          } else {
            domEl = uiDomMap['Text'];
            if (!domEl) {
              const textObj = scene.objects.find(o => o.type === 'ui_text');
              if (textObj) domEl = uiDomMap[textObj.id] || uiDomMap[textObj.name];
            }
          }
          if (!domEl) {
            domEl = Object.values(uiDomMap).find(e => e.tagName === 'DIV' && e.textContent !== undefined)
                 || Object.values(uiDomMap).find(e => e.textContent !== undefined);
          }
          if (domEl) {
            domEl.textContent = newText;
          }
          const textSceneObj = scene.objects.find(o => o.type === 'ui_text' && (!targetName || o.name === targetName || o.id === targetName));
          if (textSceneObj) {
            textSceneObj.uiText = newText;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'inventory_add_item': {
          inventory.add(props.ItemId || 'Key');
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'inventory_remove_item': {
          inventory.delete(props.ItemId || 'Key');
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'audio_play_sound': {
          playBeep(props.SoundType || 'coin');
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'game_win': {
          playBeep('win');
          const m = $('msg');
          if (m) {
            m.style.display = 'block';
            m.style.backgroundColor = 'rgba(74, 222, 128, 0.9)';
            m.style.color = '#064e3b';
            m.textContent = '🎉 Победа! Все монеты собраны!';
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'game_lose': {
          playBeep('damage');
          const m = $('msg');
          if (m) {
            m.style.display = 'block';
            m.style.backgroundColor = 'rgba(239, 68, 68, 0.9)';
            m.style.color = '#fff';
            m.textContent = '💀 Игра окончена!';
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'game_restart': {
          if (player) {
            player.m.position.copyFrom(player.start);
            player.vy = 0;
          }
          executeDownstream(node, 'out_exec');
          break;
        }
        case 'debug_log': {
          console.log('[Moewshare Event]', evaluateValue(node, 'in_msg'));
          executeDownstream(node, 'out_exec');
          break;
        }
        default:
          executeDownstream(node, 'out_exec');
          break;
      }
    }

    function runGraphEvent(g, eventType, self, other) {
      if (!g || !g.nodes) return;
      activeGraph = g;
      currentSelf = self;
      currentOther = other;
      const matchType = (n) => {
        const t = n.nodeType;
        if (t !== eventType &&
            !(eventType === 'event_ui_button_clicked' && t === 'event_object_clicked') &&
            !(eventType === 'event_object_clicked' && t === 'event_ui_button_clicked')) {
          return false;
        }
        // If event is button click and this is the global scene graph, only trigger if ButtonName matches or is empty
        if (eventType === 'event_ui_button_clicked' && g === eventGraph) {
          const btnName = n.properties?.ButtonName;
          if (btnName && self && self.name !== btnName && self.id !== btnName) return false;
        }
        // If event is object click and this is the global scene graph, only trigger if ObjectName matches or is empty
        if (eventType === 'event_object_clicked' && g === eventGraph) {
          const objName = n.properties?.ObjectName;
          if (objName && self && self.name !== objName && self.id !== objName) return false;
        }
        return true;
      };
      const nodes = g.nodes.filter(matchType);
      for (const n of nodes) {
        stepCount = 0;
        executeDownstream(n, 'out_exec');
      }
    }

    function triggerEvent(eventType, self, other) {
      // 1. Global Scene Graph
      runGraphEvent(eventGraph, eventType, self, other);

      // 2. Specific target object graph (if self has its own eventGraph)
      if (self && self.eventGraph) {
        runGraphEvent(self.eventGraph, eventType, self, other);
      }
      if (other && other.eventGraph && other !== self) {
        runGraphEvent(other.eventGraph, eventType, other, self);
      }

      // 3. For general frame/timer/start events, trigger on all objects with their own eventGraph
      if (['event_begin_play', 'event_update', 'event_timer'].includes(eventType)) {
        for (const o of scene.objects) {
          if (o.eventGraph && o !== self) {
            runGraphEvent(o.eventGraph, eventType, o, null);
          }
        }
      }
    }

    function triggerInputEvent(key) {
      const match = n =>
        n.nodeType === 'event_input_pressed' &&
        n.properties && n.properties.Key &&
        n.properties.Key.toLowerCase() === key.toLowerCase();

      if (eventGraph && eventGraph.nodes) {
        activeGraph = eventGraph;
        for (const n of eventGraph.nodes.filter(match)) {
          stepCount = 0;
          executeDownstream(n, 'out_exec');
        }
      }

      for (const o of scene.objects) {
        if (o.eventGraph && o.eventGraph.nodes) {
          activeGraph = o.eventGraph;
          currentSelf = o;
          for (const n of o.eventGraph.nodes.filter(match)) {
            stepCount = 0;
            executeDownstream(n, 'out_exec');
          }
        }
      }
    }

    canvas.addEventListener('pointerdown', () => {
      const pick = sc.pick(sc.pointerX, sc.pointerY);
      if (pick && pick.hit && pick.pickedMesh) {
        const obj = scene.objects.find(o => meshMap[o.id] === pick.pickedMesh || meshMap[o.name] === pick.pickedMesh);
        if (obj) {
          triggerEvent('event_object_clicked', obj, null);
        }
      }
    });

    let timerAcc = 0;

    // Fire Begin Play
    triggerEvent('event_begin_play', null, null);

    sc.onBeforeRenderObservable.add(() => {
      const dt = Math.min(engine.getDeltaTime() / 1000, 0.05);
      currentDt = dt;
      timerAcc += dt;
      if (timerAcc >= 1.0) {
        timerAcc -= 1.0;
        triggerEvent('event_timer', null, null);
      }
      for (const s of spinners) {
        if (s.m) s.m.rotation.y += dt * (s.sp || 1.5);
        else if (s.rotation) s.rotation.y += dt * 1.5;
      }
      if (player) step(player, dt);

      // Trigger zone overlap checks for Event System
      if (player && triggers.length > 0) {
        for (let i = triggers.length - 1; i >= 0; i--) {
          const t = triggers[i];
          if (t && t.m && t.m.isEnabled() && overlap(player, t)) {
            if (!t._wasInside) {
              t._wasInside = true;
              triggerEvent('event_trigger_enter', t.o, player.o);
            }
          } else if (t) {
            t._wasInside = false;
          }
        }
      }

      // Tick Event System frame update
      triggerEvent('event_update', null, null);

      fit();
      const targetObj = (cam.followTargetId && meshMap[cam.followTargetId]) ? { m: meshMap[cam.followTargetId] } : player;
      if (targetObj && cam.follow !== false) {
        const pp = targetObj.m.position;
        if (cam.mode === 'follow3d') {
          camera.target = B.Vector3.Lerp(camera.target, new B.Vector3(pp.x, pp.y + offY, pp.z + offZ), 0.15);
        } else {
          const dist = 40;
          if (pitchDeg >= 89.5) {
            camera.position.x = pp.x;
            camera.position.y = pp.y + dist + offY;
            camera.position.z = pp.z + offZ;
            camera.setTarget(new B.Vector3(pp.x, pp.y, pp.z + offZ));
          } else if (pitchDeg <= 0.5) {
            camera.position.x = pp.x;
            camera.position.y = pp.y + 1 + offY;
            camera.position.z = -dist + offZ;
            camera.setTarget(new B.Vector3(pp.x, pp.y + 1 + offY, offZ));
          } else {
            const cy = dist * Math.sin(pitchRad);
            const cz = -dist * Math.cos(pitchRad) * Math.cos(yawRad);
            const cx = dist * Math.cos(pitchRad) * Math.sin(yawRad);
            camera.position.x = pp.x + cx;
            camera.position.y = pp.y + cy + offY;
            camera.position.z = pp.z + cz + offZ;
            camera.setTarget(new B.Vector3(pp.x, pp.y + offY, pp.z));
          }
        }
      }
    });
    engine.runRenderLoop(() => sc.render());
    addEventListener('resize', () => engine.resize());
    canvas.focus();
  }

  window.Moewshare = { start: o => start(o).catch(e => fail(e.message)) };
})();
