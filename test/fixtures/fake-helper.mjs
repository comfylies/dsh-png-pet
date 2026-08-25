import readline from 'node:readline'

process.stdout.write('{"version":3,"kind":"ready"}\n')

const input = readline.createInterface({ input: process.stdin })
input.on('line', (line) => {
  const message = JSON.parse(line)
  if (message.version === 3 && message.kind === 'shutdown') {
    process.stdout.write('{"version":3,"kind":"closed"}\n')
    input.close()
    process.exit(0)
  }
})
