// RNW 0.84 expects PowerShell 7 while this machine only has Windows PowerShell.
// Generation scripts used by init-windows are compatible with Windows PowerShell.
const {execFileSync} = require('node:child_process');
const finder = require('@react-native-windows/find-dotnet-tools');

finder.findPowerShell = () => {
  try {
    return execFileSync('where.exe', ['pwsh.exe'], {encoding: 'utf8'}).trim().split(/\r?\n/)[0];
  } catch {
    return 'powershell.exe';
  }
};
