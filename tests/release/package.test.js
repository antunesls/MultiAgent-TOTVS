const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const os = require('node:os');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const generator = path.join(root, 'scripts/Gerar-Release.ps1');

function rejected(args, message) {
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File', generator, ...args], { encoding: 'utf8', cwd: root });
  assert.notEqual(result.status, 0);
  assert.match(result.stdout + result.stderr, message);
}

test('gerador rejeita tag malformada antes de publicar', () => {
  rejected(['-Tag', 'main'], /Tag invalida/);
});

test('gerador rejeita tag diferente da versão do projeto', () => {
  rejected(['-Tag', 'v999.0.0'], /nao corresponde a versao do projeto/);
});

test('gerador não sobrescreve uma saída existente', () => {
  rejected(['-OutputDirectory', path.join(root, 'tests')], /diretorio de saida ja existe/);
});

test('gerador exige compilador válido quando o instalador foi solicitado', () => {
  rejected(['-IsccPath', path.join(root, 'compilador-inexistente.exe'), '-OutputDirectory', path.join(root, 'artifacts', `release-${Date.now()}`)], /Inno Setup 6 nao encontrado/);
});

test('falha de publicação não libera uma release parcial', () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'multagent-release-test-'));
  const output = path.join(temporary, 'release');
  const quote = value => `'${value.replaceAll("'", "''")}'`;
  try {
    const command = `function dotnet { $global:LASTEXITCODE = 17 }; & ${quote(generator)} -SomenteZip -OutputDirectory ${quote(output)}`;
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], { encoding: 'utf8', cwd: root });
    assert.notEqual(result.status, 0);
    assert.match(result.stdout + result.stderr, /Falha no dotnet publish/);
    assert.equal(fs.existsSync(output), false);
  } finally {
    fs.rmdirSync(temporary);
  }
});

test('instalador é por usuário e não remove dados ou encerra WebAgents', () => {
  const installer = read('packaging/windows/MultAgentTOTVS.iss');
  assert.match(installer, /^PrivilegesRequired=lowest$/m);
  assert.match(installer, /^DefaultDirName=\{localappdata\}\\Programs\\MultAgent TOTVS$/m);
  assert.match(installer, /^ArchitecturesAllowed=x64compatible$/m);
  assert.match(installer, /^RestartApplications=no$/m);
  assert.match(installer, /Name: "startup";.*Flags: unchecked/m);
  assert.doesNotMatch(installer, /\[UninstallDelete\]|\[InstallDelete\]|taskkill|Stop-Process|ProtheusWebAgentManager/i);
});

test('workflow testa antes de publicar e limita escrita ao job de release', () => {
  const workflow = read('.github/workflows/release.yml');
  assert.match(workflow, /dotnet test/);
  assert.match(workflow, /npm run test:ui/);
  assert.match(workflow, /npm run test:release/);
  assert.match(workflow, /installer-smoke\.ps1/);
  assert.match(workflow, /needs: build/);
  assert.match(workflow, /contents: read/);
  assert.match(workflow, /contents: write/);
  assert.match(workflow, /github\.ref_type == 'tag'/);
});

test('launcher portátil não compila nem contorna política PowerShell', () => {
  const launcher = read('packaging/windows/portable/Iniciar.ps1');
  const command = read('packaging/windows/portable/Iniciar.cmd');
  assert.match(launcher, /api\/bootstrap/);
  assert.match(launcher, /WebAgentManager\.exe/);
  assert.doesNotMatch(launcher + command, /dotnet publish|ExecutionPolicy|Bypass/i);
});
