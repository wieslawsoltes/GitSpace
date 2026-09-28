import LightningFS from '@isomorphic-git/lightning-fs';
import http from 'isomorphic-git/http/web';
import { createBackend } from './backend.mjs';

const fs = new LightningFS('gitspace-v1');
const backend = createBackend(fs, http);
self.addEventListener('message', async ({ data }) => {
  const { id, request } = data;
  try {
    // One origin-wide repository lock prevents concurrent tabs from interleaving Git writes.
    const run = () => backend.execute(request);
    const result = self.navigator?.locks ? await navigator.locks.request('gitspace-repositories-v1', run) : await run();
    self.postMessage({ id, result });
  } catch (error) {
    // Never echo the command object: it can contain a session-only access token.
    let message = String(error.message || error);
    if (request.token) message = message.split(request.token).join('[redacted]');
    self.postMessage({ id, error: message });
  }
});
