// Serves the interface with mock.js on localhost, so it can be tested in a plain browser.
const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', '..', 'src', 'TRStudio.App', 'wwwroot');
const types = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.svg': 'image/svg+xml' };

http.createServer((request, response) => {
  const name = path.basename(new URL(request.url, 'http://localhost').pathname) || 'index.html';
  const file = name === 'mock.js' ? path.join(__dirname, name) : path.join(root, name);
  fs.readFile(file, (error, data) => {
    if (error) { response.writeHead(404).end(); return; }
    response.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream' }).end(data);
  });
}).listen(4173, '127.0.0.1');
