export type Resolution = '1280x720' | '1920x1080';
export type ShaderOptimization = 'None' | 'Size' | 'Performance';
export type LogDirection = 'Silent' | 'Console' | 'File';
export type ProfilerDirection = 'None' | 'Network';

export interface EmulatorSettings {
  screenResolution: Resolution;
  vblankFrequency: number;
  vulkanValidation: boolean;
  shaderValidation: boolean;
  shaderOptimization: ShaderOptimization;
  shaderLogDirection: LogDirection;
  shaderLogFolder: string;
  commandBufferDump: boolean;
  commandBufferDumpFolder: string;
  printfDirection: LogDirection;
  printfOutputFile: string;
  profilerDirection: ProfilerDirection;
  renderDoc: boolean;
  nggRectlistDraw: boolean;
}

export interface GameMetadata {
  titleName: string;
  titleId: string;
  gameVersion: string;
  firmwareVersion: string;
}

export interface GameInstall extends GameMetadata {
  baseDirectory: string;
  gamePath: string;
  ebootPath: string;
  artworkPath?: string;
  executable: string;
  customSettings: boolean;
  settings: EmulatorSettings;
}

export interface LauncherSettings {
  schemaVersion: 1;
  gameDirectories: string[];
  global: EmulatorSettings;
  perGame: Record<string, EmulatorSettings>;
}

export const DEFAULT_EMULATOR_SETTINGS: EmulatorSettings = {
  screenResolution: '1280x720',
  vblankFrequency: 60,
  vulkanValidation: true,
  shaderValidation: true,
  shaderOptimization: 'Performance',
  shaderLogDirection: 'Silent',
  shaderLogFolder: '_Shaders',
  commandBufferDump: false,
  commandBufferDumpFolder: '_Buffers',
  printfDirection: 'Silent',
  printfOutputFile: '_kyty.txt',
  profilerDirection: 'None',
  renderDoc: false,
  nggRectlistDraw: true,
};

export const DEFAULT_LAUNCHER_SETTINGS: LauncherSettings = {
  schemaVersion: 1,
  gameDirectories: [],
  global: {...DEFAULT_EMULATOR_SETTINGS},
  perGame: {},
};
