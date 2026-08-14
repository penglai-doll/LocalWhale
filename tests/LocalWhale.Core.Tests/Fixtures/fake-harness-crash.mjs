import { createServer } from 'node:http';

const token = process.env.LOCALWHALE_BRIDGE_TOKEN;
const version = process.env.LOCALWHALE_HARNESS_VERSION ?? 'unknown';
const server = createServer((request, response) => {
  if (request.headers['x-localwhale-token'] !== token) {
    response.writeHead(401, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ error: 'unauthorized' }));
    return;
  }
  if (request.url === '/__localwhale/v1/health') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ status: 'ok', protocolVersion: 1, harnessVersion: version, pid: process.pid }));
    return;
  }
  response.writeHead(404).end();
});

server.listen(0, '127.0.0.1', () => {
  const address = server.address();
  console.log(`dsh web: http://127.0.0.1:${address.port}`);
  setTimeout(() => process.exit(23), 300);
});
