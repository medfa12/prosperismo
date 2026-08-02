import type {LauncherSettings} from './models';
import {DEFAULT_EMULATOR_SETTINGS, DEFAULT_LAUNCHER_SETTINGS} from './models';
import type {ProsperismoHostGateway} from './host';
import {windowsPathKey} from './paths';

export function sanitizeSettings(input: unknown): LauncherSettings {
  if (!input || typeof input !== 'object') {
    return {
      ...DEFAULT_LAUNCHER_SETTINGS,
      global: {...DEFAULT_EMULATOR_SETTINGS},
      gameDirectories: [],
      perGame: {},
    };
  }
  const value = input as Partial<LauncherSettings>;
  const gameDirectories = Array.isArray(value.gameDirectories)
    ? [...new Set(value.gameDirectories.filter(item => typeof item === 'string' && item.trim()).map(item => item.trim()))]
    : [];
  return {
    schemaVersion: 1,
    gameDirectories,
    global: {...DEFAULT_EMULATOR_SETTINGS, ...(value.global ?? {})},
    perGame: value.perGame && typeof value.perGame === 'object' ? value.perGame : {},
  };
}

export async function loadSettings(host: ProsperismoHostGateway): Promise<LauncherSettings> {
  const json = await host.loadLauncherSettings();
  if (!json) {
    return sanitizeSettings(undefined);
  }
  try {
    return sanitizeSettings(JSON.parse(json));
  } catch {
    return sanitizeSettings(undefined);
  }
}

export async function saveSettings(
  host: ProsperismoHostGateway,
  settings: LauncherSettings,
): Promise<void> {
  await host.saveLauncherSettings(JSON.stringify(settings, null, 2));
}

export function setPerGameSettings(
  settings: LauncherSettings,
  gamePath: string,
  value: LauncherSettings['global'] | undefined,
): LauncherSettings {
  const perGame = {...settings.perGame};
  const key = windowsPathKey(gamePath);
  if (value) {
    perGame[key] = {...value};
  } else {
    delete perGame[key];
  }
  return {...settings, perGame};
}
