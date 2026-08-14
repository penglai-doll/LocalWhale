import { timingSafeEqual } from 'node:crypto';

export const name = 'localwhale-desktop-bridge';
export const inject = ['webServer'];
export const protocolVersion = 1;

export function createHealthPayload(harnessVersion, pid = process.pid) {
  return {
    status: 'ok',
    protocolVersion,
    harnessVersion,
    pid
  };
}

export function apply(ctx, config = {}) {
  const token = requireNonEmpty(config.token ?? process.env.LOCALWHALE_BRIDGE_TOKEN, 'bridge token');
  const harnessVersion = requireNonEmpty(
    config.harnessVersion ?? process.env.LOCALWHALE_HARNESS_VERSION ?? 'unknown',
    'harness version'
  );
  const pid = Number.isSafeInteger(config.pid) ? config.pid : process.pid;

  register(ctx, {
    kind: 'exact',
    path: '/__localwhale/v1/health',
    handler: async (request, response) => {
      if (!authorize(request, token, response)) return;
      if (request.method !== 'GET') {
        response.setHeader('Allow', 'GET');
        sendJson(response, 405, { error: 'method-not-allowed' });
        return;
      }

      sendJson(response, 200, createHealthPayload(harnessVersion, pid));
    }
  });

  register(ctx, {
    kind: 'exact',
    path: '/__localwhale/v1/shutdown',
    handler: async (request, response) => {
      if (!authorize(request, token, response)) return;
      if (request.method !== 'POST') {
        response.setHeader('Allow', 'POST');
        sendJson(response, 405, { error: 'method-not-allowed' });
        return;
      }

      sendJson(response, 202, { status: 'shutting-down' });
      setImmediate(() => {
        Promise.resolve(ctx.root.fiber.dispose()).catch((error) => {
          ctx.logger?.error?.('LocalWhale bridge shutdown failed: %o', error);
          process.exitCode = 1;
        });
      });
    }
  });
}

function register(ctx, route) {
  if (typeof ctx.effect === 'function') {
    ctx.effect(() => ctx.webServer.register(route), `localwhale bridge: ${route.path}`);
  } else {
    ctx.webServer.register(route);
  }
}

function authorize(request, token, response) {
  const supplied = request.headers?.['x-localwhale-token'];
  const candidate = Array.isArray(supplied) ? supplied[0] : supplied;
  if (typeof candidate === 'string' && secureEquals(candidate, token)) return true;
  sendJson(response, 401, { error: 'unauthorized' });
  return false;
}

function secureEquals(left, right) {
  const leftBuffer = Buffer.from(left, 'utf8');
  const rightBuffer = Buffer.from(right, 'utf8');
  return leftBuffer.length === rightBuffer.length && timingSafeEqual(leftBuffer, rightBuffer);
}

function sendJson(response, statusCode, payload) {
  response.statusCode = statusCode;
  response.setHeader('Content-Type', 'application/json; charset=utf-8');
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('X-Content-Type-Options', 'nosniff');
  response.end(JSON.stringify(payload));
}

function requireNonEmpty(value, label) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`LocalWhale ${label} is required.`);
  }
  return value;
}
