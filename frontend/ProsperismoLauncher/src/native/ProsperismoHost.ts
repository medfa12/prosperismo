import {NativeModules} from 'react-native';
import type {
  DirectoryEntry,
  LaunchRequest,
  ProsperismoHostGateway,
} from '../core/host';

interface NativeProsperismoHost {
  listDirectory(path: string): Promise<DirectoryEntry[]>;
  readTextFile(path: string): Promise<string>;
  canonicalizePath(path: string): Promise<string>;
  chooseGameDirectories(): Promise<string[]>;
  loadLauncherSettings(): Promise<string | null>;
  saveLauncherSettings(json: string): Promise<void>;
  findEmulator(): Promise<string>;
  fileExists(path: string): Promise<boolean>;
  launch(executable: string, args: string[], workingDirectory: string): Promise<void>;
}

const native = NativeModules.ProsperismoHost as NativeProsperismoHost | undefined;
const unavailable = (): never => {
  throw new Error(
    'ProsperismoHost native module is not installed. Build the Windows host adapter described in src/native/README.md.',
  );
};

export const prosperismoHost: ProsperismoHostGateway = {
  listDirectory: path => native?.listDirectory(path) ?? Promise.reject(unavailable()),
  readTextFile: path => native?.readTextFile(path) ?? Promise.reject(unavailable()),
  canonicalizePath: path => native?.canonicalizePath(path) ?? Promise.resolve(path),
  chooseGameDirectories: () =>
    native?.chooseGameDirectories() ?? Promise.reject(unavailable()),
  loadLauncherSettings: () =>
    native?.loadLauncherSettings() ?? Promise.resolve(null),
  saveLauncherSettings: json =>
    native?.saveLauncherSettings(json) ?? Promise.reject(unavailable()),
  findEmulator: () => native?.findEmulator() ?? Promise.reject(unavailable()),
  fileExists: path => native?.fileExists(path) ?? Promise.resolve(false),
  launch: (request: LaunchRequest) =>
    native?.launch(request.executable, request.args, request.workingDirectory) ??
    Promise.reject(unavailable()),
};

export const hasNativeProsperismoHost = Boolean(native);
