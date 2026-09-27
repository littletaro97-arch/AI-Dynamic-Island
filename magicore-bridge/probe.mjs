import { connectMagicoreSDK } from '@magicore/sdk';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const tokenPath = path.join(os.homedir(), '.magicore', 'data', 'control-grpc', 'bearer_token');
const sdk = await connectMagicoreSDK({
  endpoint: { kind: 'grpc' },
  grpc: { bearerToken: () => fs.readFileSync(tokenPath, 'utf8').trim() }
});
try {
  await sdk.ensureConnected();
  const agents = await sdk.agent.listPage({ limit: 100, listed: true });
  const conversations = [];
  for (const agent of agents.items) {
    const page = await sdk.conversation.listViewsPage({ agent_id: agent.agent_id, limit: 100 });
    for (const item of page.items) conversations.push({ ...item, agent_name: agent.display.name });
  }
  conversations.sort((a, b) => Date.parse(b.updated_at) - Date.parse(a.updated_at));
  const safe = conversations.slice(0, 15).map(({ conversation_id, agent_id, agent_name, title, status, last_entry_preview, last_entry_at, updated_at, active_execution_id }) => ({
    conversation_id, agent_id, agent_name, title, status, last_entry_preview, last_entry_at, updated_at,
    active: Boolean(active_execution_id)
  }));
  console.log(JSON.stringify({ ok: true, items: safe }, null, 2));
} finally {
  await sdk.close();
}
