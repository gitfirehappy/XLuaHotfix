import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const run = (args) => {
  const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
};
const sdks = run(['--list-sdks']).stdout.trim().split(/\r?\n/);
const sdk = sdks.at(-1).match(/^(\S+) \[(.+)\]$/);
const dotnetRoot = path.dirname(sdk[2]);
const refRoot = path.join(dotnetRoot, 'packs/Microsoft.NETCore.App.Ref');
const version = fs.readdirSync(refRoot).filter(x => x.startsWith('10.')).sort().at(-1);
const references = path.join(refRoot, version, 'ref/net10.0');
const scratch = fs.mkdtempSync(path.join(root, 'Temp/SerializationScenario-'));
try {
  const sources = [
    'tests/scenario/serialization/scenarios.cs',
    'Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionNumber.cs',
    ...['BinarySerializableAttribute', 'BinaryReflectionSerializer', 'BinaryWriterExt', 'BinaryReaderExt',
      'BinaryHeader', 'BinaryCodec', 'ISerializationCodec', 'SerializationHashUtility']
      .map(x => `Assets/Tools/Scripts/Serialization/${x}.cs`),
  ];
  const output = path.join(scratch, 'Scenarios.dll');
  const rsp = path.join(scratch, 'compile.rsp');
  fs.writeFileSync(rsp, ['-nologo', '-target:exe', '-langversion:9', `-out:"${output}"`,
    ...fs.readdirSync(references).filter(x => x.endsWith('.dll')).map(x => `-r:"${path.join(references, x)}"`),
    ...sources.map(x => `"${path.join(root, x)}"`)].join('\n'));
  const compilation = run([path.join(sdk[2], sdk[1], 'Roslyn/bincore/csc.dll'), `@${rsp}`]);
  process.stdout.write(compilation.stdout + compilation.stderr);
  if (compilation.status !== 0) process.exitCode = compilation.status ?? 1;
  else {
    fs.writeFileSync(path.join(scratch, 'Scenarios.runtimeconfig.json'), JSON.stringify({ runtimeOptions: {
      tfm: 'net10.0', framework: { name: 'Microsoft.NETCore.App', version: '10.0.0' }
    } }));
    const result = run([output, ...process.argv.slice(2)]);
    process.stdout.write(result.stdout + result.stderr);
    process.exitCode = result.status ?? 1;
  }
} finally {
  fs.rmSync(scratch, { recursive: true, force: true });
}
