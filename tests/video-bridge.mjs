import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../app/VideoBridge.js', import.meta.url), 'utf8');

class Element {
  constructor(tagName = 'DIV') { this.tagName = tagName; this.nodeType = 1; this.attributes = new Map(); this.children = []; this.parentElement = null; this.parentNode = null; }
  setAttribute(key, value) { this.attributes.set(key, value); }
  removeAttribute(key) { this.attributes.delete(key); }
  appendChild(node) { this.children.push(node); node.parentNode = node.parentElement = this; }
  removeChild(node) { this.children = this.children.filter(child => child !== node); node.parentNode = node.parentElement = null; }
  querySelector(selector) {
    const tags = selector.split(',').map(tag => tag.toUpperCase());
    for (const child of this.children) {
      if (tags.includes(child.tagName)) return child;
      const descendant = child.querySelector(selector);
      if (descendant) return descendant;
    }
    return null;
  }
}

function video(properties = {}) {
  return Object.assign(new Element('VIDEO'), {
    isConnected: true, paused: true, ended: false, currentTime: 12, duration: 30,
    videoWidth: 640, videoHeight: 360, readyState: 4, main: true,
    seekable: { length: 0 }, inlineStyle: 'width:640px',
    getBoundingClientRect() { return { width: this.videoWidth, height: this.videoHeight }; },
    closest() { return this.main ? this.parentElement : null; },
    play() { this.paused = false; return Promise.resolve(); },
    pause() { this.paused = true; }
  }, properties);
}

function page(videos = []) {
  const html = new Element('HTML');
  const head = new Element('HEAD');
  const body = new Element('BODY');
  html.appendChild(head); html.appendChild(body);
  for (const target of videos) { const wrap = new Element(); body.appendChild(wrap); wrap.appendChild(target); }
  let observer;
  const document = { documentElement: html, head, body,
    querySelectorAll: selector => videos.filter(target => target.isConnected && selector.split(',').some(tag => tag.toUpperCase() === target.tagName)),
    createElement: tag => new Element(tag.toUpperCase()) };
  const window = { innerWidth: 1024, innerHeight: 768, setTimeout,
    getComputedStyle: target => target.computedStyle || { display: 'block', visibility: 'visible', opacity: '1' } };
  const context = vm.createContext({ window, document, MutationObserver: class { constructor(callback) { observer = callback; } observe() {} } });
  vm.runInContext(source, context);
  return { bridge: window.__eaghBridge, html, head, body, videos, context,
    replace(target) { const wrap = new Element(); body.appendChild(wrap); wrap.appendChild(target); videos.push(target); observer([{ addedNodes: [target], removedNodes: [] }]); } };
}

test('viewing controls modify the original video and clamp its duration', () => {
  const target = video(); const { bridge } = page([target]);
  assert.equal(bridge.execute('seekBackward').currentTime, 7);
  assert.equal(bridge.execute('seekForward').currentTime, 12);
  target.currentTime = 2; assert.equal(bridge.execute('seekBackward').currentTime, 0);
  target.currentTime = 29; assert.equal(bridge.execute('seekForward').currentTime, 30);
  bridge.execute('togglePause'); assert.equal(target.paused, false);
  bridge.execute('togglePause'); assert.equal(target.paused, true);
});

test('seeking avoids gaps in discontinuous seekable ranges', () => {
  const target = video({ duration: Infinity, currentTime: 8, seekable: { length: 2, start: i => [0, 20][i], end: i => [10, 40][i] } });
  assert.equal(page([target]).bridge.execute('seekForward').currentTime, 10);
});

test('live streams without seek ranges and missing videos report visible errors', () => {
  assert.equal(page([video({ duration: Infinity })]).bridge.execute('seekForward').error, 'not_seekable');
  assert.equal(page().bridge.execute('togglePause').error, 'no_video');
  assert.equal(page([video()]).bridge.execute('unexpected').error, 'unknown_command');
});

test('a blocked play promise is reflected in bridge status', async () => {
  const target = video({ play: () => Promise.reject(new Error('autoplay blocked')) });
  const { bridge } = page([target]);
  bridge.execute('togglePause');
  await Promise.resolve();
  assert.equal(bridge.state().lastError.error, 'play_blocked');
});

test('player selection ignores hidden videos and prefers the Bilibili player', () => {
  const unrelated = video({ main: false, paused: false, currentTime: 25 });
  const hidden = video({ paused: false, computedStyle: { display: 'none', opacity: '1' }, currentTime: 50 });
  const main = video({ currentTime: 10 });
  const { bridge } = page([unrelated, hidden, main]);
  bridge.execute('seekBackward');
  assert.equal(main.currentTime, 5);
  assert.equal(unrelated.currentTime, 25);
  assert.equal(hidden.currentTime, 50);
});

test('Bilibili software-decoded video uses its standard media controls and reversible focus', () => {
  const target = video({ tagName: 'BWP-VIDEO', currentTime: 20 });
  const fixture = page([target]);
  assert.equal(fixture.bridge.execute('seekBackward').currentTime, 15);
  assert.equal(fixture.bridge.execute('seekForward').currentTime, 20);
  fixture.bridge.execute('togglePause'); assert.equal(target.paused, false);
  fixture.bridge.execute('togglePause'); assert.equal(target.paused, true);
  assert.equal(fixture.bridge.setFocus(true).focused, true);
  assert.equal(target.attributes.get('data-eagh-video'), '1');
  assert.match(fixture.head.children[0].textContent, /bwp-video\[data-eagh-video\] \*/);
  fixture.bridge.setFocus(false);
  assert.equal(target.attributes.size, 0);
  assert.equal(fixture.head.children.length, 0);
});

test('an unupgraded custom video cannot displace an operational player', () => {
  const placeholder = video({ tagName: 'BWP-VIDEO', play: undefined, pause: undefined, paused: undefined });
  const target = video({ main: false, currentTime: 15 });
  const { bridge } = page([placeholder, target]);
  assert.equal(bridge.execute('seekForward').currentTime, 20);
  assert.equal(placeholder.currentTime, 12);
  assert.equal(page([placeholder]).bridge.execute('togglePause').error, 'no_video');
});

test('focus abandons a connected hidden player before executing playback commands', () => {
  const original = video({ currentTime: 12 });
  const replacement = video({ currentTime: 25 });
  const fixture = page([original, replacement]);
  fixture.bridge.setFocus(true);
  original.computedStyle = { display: 'none', visibility: 'visible', opacity: '1' };
  fixture.bridge.execute('seekBackward');
  assert.equal(original.currentTime, 12);
  assert.equal(original.attributes.size, 0);
  assert.equal(replacement.currentTime, 20);
  assert.equal(replacement.attributes.get('data-eagh-video'), '1');
  fixture.bridge.setFocus(false);
  assert.equal(replacement.attributes.size, 0);
});

test('focus abandons a disconnected player before executing playback commands', () => {
  const original = video({ currentTime: 12 });
  const replacement = video({ currentTime: 25 });
  const fixture = page([original, replacement]);
  fixture.bridge.setFocus(true);
  original.isConnected = false;
  fixture.bridge.execute('togglePause');
  assert.equal(original.paused, true);
  assert.equal(replacement.paused, false);
  assert.equal(original.attributes.size, 0);
  assert.equal(replacement.attributes.get('data-eagh-video'), '1');
});

test('focus mode keeps original DOM parenting and inline styles and fully restores', () => {
  const target = video(); const fixture = page([target]);
  const parent = target.parentElement;
  assert.equal(fixture.bridge.setFocus(true).focused, true);
  assert.equal(target.parentElement, parent);
  assert.equal(target.inlineStyle, 'width:640px');
  assert.equal(fixture.head.children.length, 1);
  assert.match(fixture.head.children[0].textContent, /object-fit:contain/);
  assert.equal(fixture.bridge.setFocus(false).focused, false);
  assert.equal(fixture.head.children.length, 0);
  assert.equal(target.attributes.size, 0);
  assert.equal(parent.attributes.size, 0);
  assert.equal(fixture.html.attributes.size, 0);
});

test('SPA replacement reselects a newly playing video while retaining reversible focus', async () => {
  const original = video(); const fixture = page([original]);
  fixture.bridge.setFocus(true);
  const replacement = video({ paused: false, currentTime: 18 });
  fixture.replace(replacement);
  await new Promise(resolve => setTimeout(resolve, 130));
  assert.equal(fixture.bridge.state().currentTime, 18);
  assert.equal(original.attributes.size, 0);
  assert.equal(replacement.attributes.get('data-eagh-video'), '1');
  fixture.bridge.setFocus(false);
  assert.equal(replacement.attributes.size, 0);
});

test('document-created and navigation-completed double injection is idempotent', () => {
  const fixture = page([video()]);
  const initial = fixture.bridge;
  vm.runInContext(source, fixture.context);
  assert.equal(fixture.context.window.__eaghBridge, initial);
});
