const fs = require('node:fs');
const path = require('node:path');

class Logger {
  constructor(directory) {
    this.directory = directory;
    fs.mkdirSync(directory, { recursive: true });
    this.file = path.join(directory, 'killwind.log');
  }

  write(level, message, metadata = null) {
    const line = JSON.stringify({ time: new Date().toISOString(), level, message, metadata }) + '\n';
    try { fs.appendFileSync(this.file, line, 'utf8'); }
    catch (error) { console.error('Log write failed:', error.message); }
  }

  debug(message, metadata) { this.write('Debug', message, metadata); }
  info(message, metadata) { this.write('Info', message, metadata); }
  warning(message, metadata) { this.write('Warning', message, metadata); }
  error(message, metadata) { this.write('Error', message, metadata); }
}

module.exports = { Logger };
