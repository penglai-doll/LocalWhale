import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import { apply, createHealthPayload } from '../src/index.js';

test('desktop overlay inserts a new root row through the official patch dialect', async () => {
  const overlay = await readFile(new URL('../desktop-bridge.yml', import.meta.url), 'utf8');

  assert.match(overlay, /^- insert:\r?\n/m);
  assert.match(overlay, /^  - id: localwhale-desktop-bridge\r?\n/m);
  assert.match(overlay, /^    name: '@localwhale\/dsh-desktop-bridge'\r?\n/m);
});

class FakeResponse extends EventEmitter {
  statusCode = 200;
  headers = {};
  body = '';

  setHeader(name, value) {
    this.headers[name.toLowerCase()] = value;
  }

  end(body = '') {
    this.body += body;
    this.emit('finish');
  }
}

function createContext() {
  const routes = new Map();
  let disposed = false;
  const ctx = {
    webServer: {
      register(route) {
        routes.set(route.path, route.handler);
        return () => routes.delete(route.path);
      }
    },
    root: {
      fiber: {
        async dispose() {
          disposed = true;
        }
      }
    }
  };
  return { ctx, routes, wasDisposed: () => disposed };
}

test('createHealthPayload reports the stable bridge protocol', () => {
  assert.deepEqual(createHealthPayload('0.1.0-rc.6', 1234), {
    status: 'ok',
    protocolVersion: 1,
    harnessVersion: '0.1.0-rc.6',
    pid: 1234
  });
});

test('health route rejects a request without the per-launch token', async () => {
  const { ctx, routes } = createContext();
  apply(ctx, { token: 'bridge-secret', harnessVersion: '0.1.0-rc.6', pid: 1234 });
  const response = new FakeResponse();

  await routes.get('/__localwhale/v1/health')({ method: 'GET', headers: {} }, response);

  assert.equal(response.statusCode, 401);
  assert.deepEqual(JSON.parse(response.body), { error: 'unauthorized' });
});

test('health route returns protocol and process details for an authorized GET', async () => {
  const { ctx, routes } = createContext();
  apply(ctx, { token: 'bridge-secret', harnessVersion: '0.1.0-rc.6', pid: 1234 });
  const response = new FakeResponse();

  await routes.get('/__localwhale/v1/health')({
    method: 'GET',
    headers: { 'x-localwhale-token': 'bridge-secret' }
  }, response);

  assert.equal(response.statusCode, 200);
  assert.equal(response.headers['cache-control'], 'no-store');
  assert.equal(JSON.parse(response.body).protocolVersion, 1);
});

test('shutdown route accepts only an authorized POST and disposes the root fiber', async () => {
  const { ctx, routes, wasDisposed } = createContext();
  apply(ctx, { token: 'bridge-secret', harnessVersion: '0.1.0-rc.6', pid: 1234 });
  const response = new FakeResponse();

  await routes.get('/__localwhale/v1/shutdown')({
    method: 'POST',
    headers: { 'x-localwhale-token': 'bridge-secret' }
  }, response);
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(response.statusCode, 202);
  assert.deepEqual(JSON.parse(response.body), { status: 'shutting-down' });
  assert.equal(wasDisposed(), true);
});
