const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const size = 256;
const pixels = Buffer.alloc(size * size * 4);
const assets = path.join(__dirname, '..', 'assets');
fs.mkdirSync(assets, { recursive: true });

function color(hex, alpha = 255) {
  return [parseInt(hex.slice(1, 3), 16), parseInt(hex.slice(3, 5), 16), parseInt(hex.slice(5, 7), 16), alpha];
}
function setPixel(x, y, rgba) {
  if (x < 0 || y < 0 || x >= size || y >= size) return;
  const offset = (y * size + x) * 4;
  pixels.set(rgba, offset);
}
function fillRoundedRect(x, y, width, height, radius, rgba) {
  for (let py = y; py < y + height; py += 1) for (let px = x; px < x + width; px += 1) {
    const dx = Math.max(x + radius - px, 0, px - (x + width - radius - 1));
    const dy = Math.max(y + radius - py, 0, py - (y + height - radius - 1));
    if (dx * dx + dy * dy <= radius * radius) setPixel(px, py, rgba);
  }
}
function drawLine(from, to, width, rgba) {
  const distance = Math.hypot(to.x - from.x, to.y - from.y);
  const steps = Math.max(1, Math.ceil(distance));
  const radius = width / 2;
  for (let step = 0; step <= steps; step += 1) {
    const x = from.x + (to.x - from.x) * step / steps;
    const y = from.y + (to.y - from.y) * step / steps;
    for (let py = Math.floor(y - radius); py <= Math.ceil(y + radius); py += 1) for (let px = Math.floor(x - radius); px <= Math.ceil(x + radius); px += 1) {
      if ((px - x) ** 2 + (py - y) ** 2 <= radius ** 2) setPixel(px, py, rgba);
    }
  }
}
function drawBezier(points, width, rgba) {
  let previous = points[0];
  for (let step = 1; step <= 40; step += 1) {
    const t = step / 40;
    const one = 1 - t;
    const next = {
      x: one ** 3 * points[0].x + 3 * one ** 2 * t * points[1].x + 3 * one * t ** 2 * points[2].x + t ** 3 * points[3].x,
      y: one ** 3 * points[0].y + 3 * one ** 2 * t * points[1].y + 3 * one * t ** 2 * points[2].y + t ** 3 * points[3].y,
    };
    drawLine(previous, next, width, rgba);
    previous = next;
  }
}
function chunk(type, data) {
  const header = Buffer.alloc(8); header.writeUInt32BE(data.length, 0); header.write(type, 4, 4, 'ascii');
  const checksum = Buffer.alloc(4); checksum.writeUInt32BE(crc32(Buffer.concat([header.subarray(4), data])), 0);
  return Buffer.concat([header, data, checksum]);
}
function png() {
  const raw = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y += 1) { raw[y * (size * 4 + 1)] = 0; pixels.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4); }
  const header = Buffer.alloc(13); header.writeUInt32BE(size, 0); header.writeUInt32BE(size, 4); header[8] = 8; header[9] = 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) { crc ^= byte; for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1)); }
  return (crc ^ 0xffffffff) >>> 0;
}
function ico(image) {
  const header = Buffer.alloc(6); header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(1, 4);
  const entry = Buffer.alloc(16); entry[0] = 0; entry[1] = 0; entry[2] = 0; entry[3] = 0; entry.writeUInt16LE(1, 4); entry.writeUInt16LE(32, 6); entry.writeUInt32LE(image.length, 8); entry.writeUInt32LE(22, 12);
  return Buffer.concat([header, entry, image]);
}

fillRoundedRect(0, 0, size, size, 48, color('#0b1724'));
fillRoundedRect(11, 11, size - 22, size - 22, 39, color('#102a39'));
drawBezier([{ x: 66, y: 63 }, { x: 120, y: 30 }, { x: 154, y: 32 }, { x: 196, y: 55 }], 10, color('#67e6d2'));
drawBezier([{ x: 68, y: 103 }, { x: 129, y: 73 }, { x: 165, y: 82 }, { x: 204, y: 104 }], 12, color('#6ba9ff'));
drawBezier([{ x: 66, y: 146 }, { x: 116, y: 124 }, { x: 154, y: 135 }, { x: 185, y: 153 }], 9, color('#67e6d2'));
drawLine({ x: 67, y: 61 }, { x: 67, y: 203 }, 22, color('#e7f0f5'));
drawLine({ x: 72, y: 130 }, { x: 161, y: 58 }, 22, color('#e7f0f5'));
drawLine({ x: 72, y: 130 }, { x: 168, y: 204 }, 22, color('#e7f0f5'));
fs.writeFileSync(path.join(assets, 'killwind.ico'), ico(png()));
console.log('KillWind icon generated.');
