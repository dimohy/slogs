# Organization administration console

For trusted Slogs server operators only. This is not a login mechanism or a network service. Possession of protected production database configuration and server administration access is required. Never wire it to HTTP/MCP, give its database access to the chatbot, or use it to bypass a tenant API denial.

The console invokes existing canonical services. Organization provisioning checks existing administrator and owner accounts, refuses mismatched organizations, and verifies active owner membership plus the creation audit. It does not change roles or run migrations. Its explicit `reader-token` and `registration-token` modes issue organization-scoped tokens; never run these modes interactively or redirect token output to plaintext logs. Use the protected DPAPI wrapper in the Ain demo.

Run `plan actor slug displayName owner environmentLabel` first, inspect the output, then run identical arguments with `apply`. Configuration comes only from `ConnectionStrings__SlogsDatabase`, inherited inside the production container; no default, credentials in arguments, or credential output is permitted. Do not redeploy the Slogs app to run this console.

## Ain corpus workflow

`build-corpus inputDirectory outputPath` creates the versioned natural-unit chunk and source-explicit relation plan. The remaining modes take `planPath actorName` and require the existing active Ain organization owner:

- `status-corpus`: read-only complete comparison of every persisted chunk against the canonical NFKC-normalized plan (ID, text, document, locators, aliases, metadata, hash and embedding identity); reports verified and remaining counts.
- `ingest-corpus`: perform the same comparison, fail on mismatches or unexpected chunks, skip identical chunks, ingest only missing chunks through KnowledgeCorpusService, and activate only after integrity validation. Never reset or delete existing data.
- `verify-corpus`: require every expected chunk to match, then require the exact active version and document/relation counts. Returns the actual database content hash.

Corpus modes read the production app settings and inherited environment inside the trusted server container. They do not expose that configuration or expand chatbot token permissions. The original source plan remains unmodified; normalization is the same canonical ingestion contract used by Slogs.
