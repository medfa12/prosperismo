import {DeviceEventEmitter, NativeModules} from 'react-native';
import type {
  DestructiveDirectoryRequest,
  DirectoryEntry,
  HostProcessEvent,
  LaunchRequest,
  ProsperismoHostGateway,
} from '../core/host';

interface NativeProsperismoHost {
  listDirectory(path: string): Promise<DirectoryEntry[]>;
  readTextFile(path: string): Promise<string>;
  readBinaryFile(path: string): Promise<number[]>;
  writeTextFile(path: string, contents: string): Promise<void>;
  canonicalizePath(path: string): Promise<string>;
  chooseGameDirectories(): Promise<string[]>;
  loadLauncherSettings(): Promise<string | null>;
  saveLauncherSettings(json: string): Promise<void>;
  findEmulator(): Promise<string>;
  fileExists(path: string): Promise<boolean>;
  setBigPictureMode(enabled: boolean): Promise<void>;
  openPath(path: string): Promise<void>;
  removeDirectories(paths: string[], titleId: string, confirmed: boolean): Promise<string[]>;
  launch(executable: string, args: string[], workingDirectory: string): Promise<void>;
}

const native = NativeModules.ProsperismoHost as NativeProsperismoHost | undefined;
const unavailable = (): Error =>
  new Error(
    'ProsperismoHost native module is not installed. Build the Windows host adapter described in src/native/README.md.',
  );

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
  ...(native
    ? {
        writeTextFile: (path: string, contents: string) =>
          native.writeTextFile(path, contents),
        readBinaryFile: async (path: string) =>
          Uint8Array.from(await native.readBinaryFile(path)),
        openPath: (path: string) => native.openPath(path),
        removeDirectories: (request: DestructiveDirectoryRequest) =>
          native.removeDirectories(request.paths, request.titleId, request.confirmed),
        subscribeProcessEvents: (listener: (event: HostProcessEvent) => void) => {
          const subscription = DeviceEventEmitter.addListener(
            'ProsperismoHostProcess',
            listener,
          );
          return () => subscription.remove();
        },
      }
    : {}),
};

export const hasNativeProsperismoHost = Boolean(native);

export const setBigPictureMode = (enabled: boolean): Promise<void> =>
  native?.setBigPictureMode(enabled) ?? Promise.resolve();
