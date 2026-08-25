import readline from 'node:readline'

process.stdout.write('{"version":2,"kind":"ready"}\n')

const input = readline.createInterface({ input: process.stdin })
input.on('line', (line) => {
  const message = JSON.parse(line)
  if (message.version === 2 && message.kind === 'shutdown') {
    process.stdout.write('{"version":2,"kind":"closed"}\n')
    input.close()
    process.exit(0)
  }
})
