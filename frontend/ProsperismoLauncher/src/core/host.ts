export interface DirectoryEntry {
  name: string;
  path: string;
  kind: 'file' | 'directory';
  symbolicLink?: boolean;
}

export interface LaunchRequest {
  executable: string;
  args: string[];
  workingDirectory: string;
}

export interface ProsperismoHostGateway {
  listDirectory(path: string): Promise<DirectoryEntry[]>;
  readTextFile(path: string): Promise<string>;
  canonicalizePath(path: string): Promise<string>;
  chooseGameDirectories(): Promise<string[]>;
  loadLauncherSettings(): Promise<string | null>;
  saveLauncherSettings(json: string): Promise<void>;
  findEmulator(): Promise<string>;
  fileExists(path: string): Promise<boolean>;
  launch(request: LaunchRequest): Promise<void>;
}
