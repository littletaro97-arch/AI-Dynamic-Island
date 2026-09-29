import { connectMagicoreSDK } from '@magicore/sdk';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const tokenPath = path.join(os.homedir(), '.magicore', 'data', 'control-grpc', 'bearer_token');
const outputPath = process.argv[2];
const emit = value => {
  const payload = JSON.stringify(value);
  if (outputPath) fs.writeFileSync(outputPath, payload, 'utf8');
  else console.log(payload);
};
let sdk;

try {
  sdk = await connectMagicoreSDK({
    endpoint: { kind: 'grpc' },
    grpc: { bearerToken: () => fs.readFileSync(tokenPath, 'utf8').trim() }
  });
  await sdk.ensureConnected();

  const agents = await sdk.agent.listPage({ limit: 100, listed: true });
  const conversations = [];
  for (const agent of agents.items) {
    const page = await sdk.conversation.listViewsPage({ agent_id: agent.agent_id, status: 'active', limit: 100 });
    for (const item of page.items) {
      conversations.push({ ...item, agent_name: agent.display.name });
    }
  }

  conversations.sort((a, b) => {
    const bTime = Date.parse(b.last_entry_at || b.updated_at || b.created_at);
    const aTime = Date.parse(a.last_entry_at || a.updated_at || a.created_at);
    return bTime - aTime;
  });

  const active = conversations.filter(item => Boolean(item.active_execution_id));
  let busy = false;
  for (const item of active) {
    const thread = await sdk.conversation.threadView({ conversation_id: item.conversation_id });
    const execution = thread.live_execution || thread.active_execution_view;
    if (execution && !['completed', 'failed', 'cancelled', 'incomplete'].includes(execution.status)) {
      busy = true;
      break;
    }
  }

  const latest = conversations[0];
  let latestReply = '';
  if (latest?.conversation_id) {
    try {
      const history = await sdk.conversation.historyPage({
        conversation_id: latest.conversation_id,
        anchor: 'tail',
        direction: 'backward',
        limit: 24
      });
      const replies = (history.items || []).filter(item => item.entry_kind === 'agent_message');
      const reply = replies.sort((a, b) => {
        try { return Number(BigInt(String(b.seq ?? 0)) - BigInt(String(a.seq ?? 0))); }
        catch { return Date.parse(b.created_at || 0) - Date.parse(a.created_at || 0); }
      })[0];
      latestReply = (reply?.parts || [])
        .filter(part => part?.type === 'text' && typeof part.text === 'string')
        .map(part => part.text)
        .join('\n');
    } catch {
      // Older Magicore builds may not expose conversation history; keep the preview fallback.
    }
  }
  const preview = String(latestReply || latest?.last_entry_preview || latest?.title || '尚未检测到任务结果')
    .replace(/\s+/g, ' ')
    .trim();
  const failed = /失败|出错|无法完成|\berror\b|\bfailed\b/i.test(preview);

  emit({
    ok: true,
    source: 'magicore',
    busy,
    recentResult: preview,
    lastTaskFailed: failed,
    updatedAt: latest?.last_entry_at || latest?.updated_at || null,
    conversationId: latest?.conversation_id || null,
    agentName: latest?.agent_name || null
  });
} catch (error) {
  emit({
    ok: false,
    source: 'magicore',
    error: error instanceof Error ? error.name : 'MagicoreError'
  });
} finally {
  if (sdk) await sdk.close().catch(() => undefined);
}
