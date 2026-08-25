import readline from 'node:readline'

process.stdout.write('{"version":4,"kind":"ready"}\n')

if (process.argv.includes('--input')) {
  process.stdout.write('{"version":4,"kind":"input","requestId":9,"text":"hello"}\n')
}

if (process.argv.includes('--out-of-order-inputs')) {
  process.stdout.write('{"version":4,"kind":"input","requestId":9,"text":"first"}\n')
  process.stdout.write('{"version":4,"kind":"input","requestId":9,"text":"duplicate"}\n')
  process.stdout.write('{"version":4,"kind":"input","requestId":8,"text":"older"}\n')
  process.stdout.write('{"version":4,"kind":"input","requestId":10,"text":"later"}\n')
}

if (process.argv.includes('--retry-input')) {
  process.stdout.write('{"version":4,"kind":"input","requestId":11,"text":"first"}\n')
  process.stdout.write('{"version":4,"kind":"input","requestId":11,"text":"retry"}\n')
  process.stdout.write('{"version":4,"kind":"input","requestId":12,"text":"later"}\n')
}

const input = readline.createInterface({ input: process.stdin })
input.on('line', (line) => {
  const message = JSON.parse(line)
  if (message.version === 4 && message.kind === 'shutdown') {
    process.stdout.write('{"version":4,"kind":"closed"}\n')
    input.close()
    process.exit(0)
  }
})
