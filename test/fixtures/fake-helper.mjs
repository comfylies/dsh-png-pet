import readline from 'node:readline'

process.stdout.write('{"version":12,"kind":"ready"}\n')

if (process.argv.includes('--exit-after-ready')) {
  setTimeout(() => process.exit(7), 25)
}

if (process.argv.includes('--input')) {
  process.stdout.write('{"version":12,"kind":"input","requestId":9,"text":"hello"}\n')
}

if (process.argv.includes('--out-of-order-inputs')) {
  process.stdout.write('{"version":12,"kind":"input","requestId":9,"text":"first"}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":9,"text":"duplicate"}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":8,"text":"older"}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":10,"text":"later"}\n')
}

if (process.argv.includes('--retry-input')) {
  process.stdout.write('{"version":12,"kind":"input","requestId":11,"text":"first"}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":11,"text":"retry"}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":12,"text":"later"}\n')
}

if (process.argv.includes('--history-then-input')) {
  process.stdout.write('{"version":12,"kind":"request-history","requestId":1}\n')
  process.stdout.write('{"version":12,"kind":"input","requestId":1,"text":"hello"}\n')
}

if (process.argv.includes('--stop')) {
  process.stdout.write('{"version":12,"kind":"stop","requestId":9}\n')
}

if (process.argv.includes('--target-open')) {
  process.stdout.write('{"version":12,"kind":"target-open","requestId":21}\n')
}

if (process.argv.includes('--target-answer')) {
  process.stdout.write('{"version":12,"kind":"target-answer","requestId":22,"sessionId":"s-1","workspaceId":"w-1","newBlank":false}\n')
}

if (process.argv.includes('--random-chat-open')) {
  process.stdout.write('{"version":12,"kind":"random-chat-open","invitationId":31,"topic":"news"}\n')
}

if (process.argv.includes('--dialogue-closed')) {
  process.stdout.write('{"version":12,"kind":"dialogue-closed"}\n')
}

const input = readline.createInterface({ input: process.stdin })
input.on('line', (line) => {
  const message = JSON.parse(line)
  if (message.version === 12 && message.kind === 'shutdown') {
    process.stdout.write('{"version":12,"kind":"closed"}\n')
    input.close()
    process.exit(0)
  }
})
