// Only Copilot's prompt transformation reads stdin. It adds fixed guidance and does not send it to Slogs.
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const endpoint = 'https://slogs.dev/mcp';
const indexLimit = 6500;
const lookup = JSON.stringify({
  jsonrpc: '2.0', id: 1, method: 'tools/call',
  params: { name: 'llm_wiki_session_corrections', arguments: {} },
});
const sessionGuidance = [
  'Slogs LLM Wiki: compare the task with private rule scopes before drafting. Read llm_wiki_instructions at session start.',
  'Read matching rules as needed. Use narrow recall and nextAfter to find missing rules. Resume pending decisions by original ID.',
  'Check the first result against each matching rule and fix mismatches before sending.',
].join(' ');
const feedbackGuidance = [
  'Slogs LLM Wiki: Before continuing, treat a correction that states a repeatable condition and what to do there as a reusable rule, even without “always”. Honor explicit one-off limits. Interpret praise or dissatisfaction against the preceding result. Save the confirmed decision and narrow scope, never the reaction or conversation.',
  'Apply feedback to the current work. For a reusable private decision, use llm_wiki_capture with correctionDecision and correctionCategoryPath, read the matching entry, then llm_wiki_commit_correction using the original IDs. Keep one-off changes, ambiguous reactions, secrets and unauthorized content out of memory.',
].join(' ');
const copilotRecallReminder = 'Slogs plugin reminder: For follow-up work governed by an earlier Slogs rule, call llm_wiki_read for that rule before drafting; it may have changed in another chat. Skip Slogs for unrelated work.';

function rpcResult(body, type) {
  if (type.startsWith('text/event-stream')) {
    for (const event of body.split(/\r?\n\r?\n/)) {
      const data = event.split(/\r?\n/).filter(line => line.startsWith('data:'))
        .map(line => line.slice(5).trimStart()).join('\n');
      if (!data) continue;
      const message = JSON.parse(data);
      if (message.id === 1) return message.result;
    }
    return;
  }
  const message = JSON.parse(body);
  if (message.id === 1) return message.result;
}

function fitIndex(page, rules) {
  const index = {
    rules, nextAfter: page.nextAfter,
    pendingCorrections: page.pendingCorrections.map(({ turnId, candidateId }) => ({ turnId, candidateId })),
  };
  let serialized = JSON.stringify(index);
  for (let i = rules.length - 1; i >= 0 && serialized.length > indexLimit; i--) {
    const rule = rules[i];
    if (!('currentContent' in rule)) continue;
    delete rule.currentContent;
    rule.fullReadRequired = true;
    serialized = JSON.stringify(index);
  }

  // The server resumes strictly after a rule ID. Advance only past rules we delivered.
  const originalCount = rules.length;
  while (serialized.length > indexLimit && rules.length > 0) {
    rules.pop();
    index.nextAfter = rules.at(-1)?.entryId ?? null;
    serialized = JSON.stringify(index);
  }
  if (serialized.length > indexLimit) throw new Error('Pending correction IDs exceed the hook limit');

  const prefix = originalCount > 0 && rules.length === 0
    ? 'Private correction lookup succeeded. Call llm_wiki_session_corrections to read the first page before drafting.'
    : 'Private correction lookup succeeded.';
  return prefix + ' Owner data:\n' + serialized;
}

async function loadContext(token, request) {
  const response = await request(endpoint, {
    method: 'POST', redirect: 'error', signal: AbortSignal.timeout(5000),
    headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' },
    body: lookup,
  });
  if (!response.ok) return 'Private lookup failed (HTTP ' + response.status + '). Check the Slogs MCP connection before claiming recall.';
  const result = rpcResult(await response.text(), response.headers.get('content-type') ?? '');
  if (!result || result.isError) throw new Error('Slogs lookup failed');
  const page = JSON.parse(result.content[0].text);
  if (page.scope !== 'owner-private' || !Array.isArray(page.rules) || !Array.isArray(page.pendingCorrections)) {
    throw new Error('Invalid correction page');
  }
  // Always preserve identity, activation and the paging cursor. Omitted bodies are read via MCP.
  const rules = page.rules.map(({ entryId, version, categoryPath, title, activation, currentContent, fullReadRequired }) => {
    const includeContent = !fullReadRequired && typeof currentContent === 'string' && currentContent.length <= 480;
    return { entryId, version, categoryPath, title, activation, fullReadRequired: !includeContent,
      ...(includeContent ? { currentContent } : {}) };
  });
  return fitIndex(page, rules);
}

export async function hookOutput(event, env = process.env, request = fetch, input) {
  if (event === 'userPromptTransformed') {
    const prompt = input?.transformedPrompt;
    if (typeof prompt !== 'string' || !prompt.trim()) return {};
    return { modifiedTransformedPrompt: prompt + '\n\n' + copilotRecallReminder };
  }
  if (event === 'UserPromptSubmit')
    return { hookSpecificOutput: { hookEventName: event, additionalContext: feedbackGuidance } };
  if (!['SessionStart', 'sessionStart'].includes(event)) throw new Error('Unsupported Slogs hook event');
  let context;
  if (env.SLOGS_WIKI_HOOK_MODE === 'host') {
    context = 'Read corrections with the enabled Slogs MCP connection before drafting. Respect its configured account and permissions.';
  } else if (!env.SLOGS_MCP_TOKEN) {
    context = 'Automatic lookup needs SLOGS_MCP_TOKEN. Use the host’s configured Slogs MCP connection if available. Never ask for credentials in chat.';
  } else {
    try {
      context = await loadContext(env.SLOGS_MCP_TOKEN, request);
    } catch {
      context = 'The automatic private lookup did not complete. Check the Slogs MCP connection and read llm_wiki_session_corrections before reporting recall.';
    }
  }
  const additionalContext = [sessionGuidance, ...(event === 'sessionStart' ? [feedbackGuidance] : []), context].join('\n');
  return event === 'sessionStart' ? { additionalContext } : { hookSpecificOutput: { hookEventName: event, additionalContext } };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const event = process.argv[2];
  const input = event === 'userPromptTransformed' ? JSON.parse(readFileSync(0, 'utf8')) : undefined;
  process.stdout.write(JSON.stringify(await hookOutput(event, process.env, fetch, input)) + '\n');
}
