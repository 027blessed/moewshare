// Дымовой тест JS-рантайма без браузера: заглушка Babylon + реальная физика рантайма.
// Запуск: node tests/runtime/runtime.test.js
const fs = require('fs'), path = require('path'), assert = require('assert');
class V3 { constructor(x=0,y=0,z=0){this.x=x;this.y=y;this.z=z;} set(x,y,z){this.x=x;this.y=y;this.z=z;return this;}
  clone(){return new V3(this.x,this.y,this.z);} copyFrom(o){return this.set(o.x,o.y,o.z);} static Zero(){return new V3();}
  static Lerp(a,b,t){return new V3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);} }
let before = null; const listeners = {}; const meshes = {};
const box = (name) => (meshes[name] = { position:new V3(), rotation:new V3(), scaling:new V3(1,1,1), material:null });
global.window = global;
global.BABYLON = {
  Vector3: V3, Color3:{FromHexString:()=>({})}, Color4:{FromHexString:()=>({})}, Camera:{ORTHOGRAPHIC_CAMERA:1},
  Engine: class{ getDeltaTime(){return 16;} getRenderWidth(){return 800;} getRenderHeight(){return 600;} runRenderLoop(){} resize(){} },
  Scene: class{ constructor(){ this.onBeforeRenderObservable={add:f=>{before=f;}}; } render(){} },
  HemisphericLight: class{}, PointLight: class{}, StandardMaterial: class{},
  MeshBuilder:{CreateBox:(n)=>box(n),CreateSphere:(n)=>box(n)},
  FreeCamera: class{ constructor(){this.position=new V3(); this.rotation=new V3();} },
  ArcRotateCamera: class{ constructor(){this.target=new V3();} attachControl(){} },
};
global.addEventListener = (n,f)=>{ (listeners[n]=listeners[n]||[]).push(f); };
global.document = { getElementById: id => id==='game'?{focus(){}}:{style:{}}, createElement:()=>({}), head:{appendChild(){}} };
const root = path.join(__dirname,'scene');
global.fetch = async p => ({ ok:true, json: async()=>JSON.parse(fs.readFileSync(path.join(root,p),'utf8')) });
const key = (type, code) => (listeners[type]||[]).forEach(f=>f({code, preventDefault(){}}));
const frames = n => { for (let i=0;i<n;i++) before(); };

require('../../src/Moewshare.Engine/Runtime/moewshare-runtime.js');
(async () => {
  Moewshare.start({canvas:'game',project:'project.json'});
  await new Promise(r=>setTimeout(r,50));
  assert(before, 'рантайм не зарегистрировал игровой цикл');
  const p = meshes.Player;
  frames(120);
  assert(Math.abs(p.position.y - (-4 + 0.5 + 0.45)) < 1e-6, 'игрок должен стоять на земле, y=' + p.position.y);
  key('keydown','ArrowRight'); frames(30); key('keyup','ArrowRight');
  assert(p.position.x > -10 + 2, 'игрок должен идти вправо, x=' + p.position.x);
  key('keydown','Space'); frames(10);
  assert(p.position.y > -3.05 + 0.5, 'игрок должен прыгнуть, y=' + p.position.y);
  key('keyup','Space'); frames(120);
  assert(Math.abs(p.position.y - (-3.05)) < 1e-6, 'игрок должен снова приземлиться, y=' + p.position.y);
  console.log('runtime OK: падение, ход, прыжок, приземление');
})().catch(e=>{ console.error('FAIL', e.message || e); process.exit(1); });
