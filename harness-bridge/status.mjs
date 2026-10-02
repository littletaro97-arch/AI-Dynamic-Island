import fs from 'node:fs';
import path from 'node:path';
import { zstdDecompressSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';

const concise = text => String(text ?? '').replace(/\s+/g, ' ').slice(0, 600);
const pendingResult = data => {
  if (data.error?.code === 'TOOL_OUTCOME_UNKNOWN') return true;
  try { return JSON.parse(data.message?.content?.find(block => block.type === 'text')?.text ?? '{}').pending === true; }
  catch { return false; }
};

// Read the versioned journal only. Never read credentials, replay IPC, or mutate a session.
export function foldSession(rows, now = Date.now()) {
  const header = rows[0];
  if (header?.type !== 'session' || header.version !== 4 || !header.id) throw new Error('unsupported-session-format');
  let open = false, state = 'idle', response = '', completions = [], updatedAt = header.createdAt;
  const approvals = new Set(), questions = new Set();
  let confirmationId = null;
  for (const event of rows.slice(1)) {
    const data = event.data ?? {};
    updatedAt = event.time ?? updatedAt;
    if (event.type === 'turn/start') { open = true; state = 'running'; approvals.clear(); response = ''; }
    if (event.type === 'approval/asked' && open) { approvals.add(data.id); confirmationId = `${header.id}|approval|${data.id}`; }
    if (event.type === 'approval/decided') approvals.delete(data.id);
    if (event.type === 'tool/call' && open && ['ask_user_questions', 'AskUserQuestion', 'ask_user_question'].includes(data.name)) {
      questions.add(data.callId); confirmationId = `${header.id}|question|${data.callId}`;
    }
    if (event.type === 'tool/result' && !pendingResult(data)) questions.delete(data.message?.toolCallId);
    if (event.type === 'user/message' && data.source?.kind === 'user-question-reply') questions.delete(data.source.callId);
    if (event.type === 'assistant/message') response = concise((data.message?.content ?? []).filter(block => block.type === 'text').map(block => block.text).join(' '));
    if (event.type === 'turn/end') {
      open = false; approvals.clear();
      const reason = data.reason?.kind;
      state = reason === 'completed' ? 'completed' : reason === 'blocked' ? 'decision' : ['cancelled', 'interrupted', 'aborted'].includes(reason) ? 'cancelled' : 'error';
      if (state === 'completed') completions.push({ id: `${header.id}|${event.seq}`, response: response || '任务已完成', completedAt: new Date(event.time).toISOString() });
    }
  }
  if ((open && approvals.size) || questions.size) state = 'decision';
  // An unclosed journal after a crash is evidence of an unresolved turn, not proof of a live task.
  if (open && now - new Date(updatedAt).getTime() > 2 * 60 * 60 * 1000) state = 'unknown';
  if (!open && ['completed', 'error', 'cancelled'].includes(state) && now - new Date(updatedAt).getTime() > 30 * 60 * 1000) state = 'idle';
  return { state, confirmationId: state === 'decision' ? confirmationId ?? `${header.id}|blocked` : null, completions: completions.slice(-32) };
}

export function readSessions(files) {
  const sessions = [], errors = [];
  for (const file of files) {
    try {
      if (fs.statSync(file).size > 8 * 1024 * 1024) throw new Error('compressed-session-too-large');
      const bytes = zstdDecompressSync(fs.readFileSync(file), { maxOutputLength: 32 * 1024 * 1024 });
      const rows = bytes.toString('utf8').trim().split('\n').map(line => JSON.parse(line));
      const legacy = path.join(path.dirname(file), 'session.jsonl.zstd');
      if (rows.length === 1 && fs.existsSync(legacy)) {
        if (fs.statSync(legacy).size > 8 * 1024 * 1024) throw new Error('legacy-session-log-requires-adapter');
        const legacyRows = zstdDecompressSync(fs.readFileSync(legacy), { maxOutputLength: 32 * 1024 * 1024 }).toString('utf8').trim().split('\n').map(line => JSON.parse(line));
        if (legacyRows.length !== 1 || legacyRows[0]?.type !== 'session' || legacyRows[0]?.id !== rows[0]?.id)
          throw new Error('legacy-session-log-requires-adapter');
      }
      sessions.push(foldSession(rows));
    } catch (error) { errors.push(error.message); }
  }
  const state = ['decision', 'running', 'error', 'unknown', 'completed', 'cancelled', 'idle'].find(candidate => sessions.some(session => session.state === candidate)) ?? 'unknown';
  return { available: sessions.length > 0 && errors.length === 0, state,
    confirmationId: sessions.find(session => session.state === 'decision')?.confirmationId ?? null,
    completions: sessions.flatMap(session => session.completions), error: errors.length ? errors[0] : null };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href)
  process.stdout.write(JSON.stringify(readSessions(process.argv.slice(2))));
