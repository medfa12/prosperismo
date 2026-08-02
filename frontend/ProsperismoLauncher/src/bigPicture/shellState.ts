import type {GameInstall} from '../core/models';

export type ShellSpace = 'games' | 'media';
export type ShellSurface = 'home' | 'library' | 'settings';
export type ShellFocusRegion = 'spaces' | 'strand' | 'system' | 'content';

export interface ShellState {
  space: ShellSpace;
  surface: ShellSurface;
  focusRegion: ShellFocusRegion;
  selectedIndex: number;
  settingsIndex: number;
  systemIndex: number;
}

export type ShellAction =
  | {type: 'focus'; region: ShellFocusRegion}
  | {type: 'select-game'; index: number; gameCount: number}
  | {type: 'move'; delta: -1 | 1; gameCount: number}
  | {type: 'open-library'}
  | {type: 'open-settings'}
  | {type: 'home'}
  | {type: 'set-space'; space: ShellSpace}
  | {type: 'select-setting'; index: number}
  | {type: 'select-system'; index: number};

export const INITIAL_SHELL_STATE: ShellState = {
  space: 'games',
  surface: 'home',
  focusRegion: 'strand',
  selectedIndex: 0,
  settingsIndex: 0,
  systemIndex: 0,
};

function clamp(value: number, length: number): number {
  return Math.max(0, Math.min(value, Math.max(0, length - 1)));
}

export function reduceShellState(state: ShellState, action: ShellAction): ShellState {
  switch (action.type) {
    case 'focus': return {...state, focusRegion: action.region};
    case 'select-game': return {...state, focusRegion: 'strand', selectedIndex: clamp(action.index, action.gameCount)};
    case 'move': return {...state, selectedIndex: clamp(state.selectedIndex + action.delta, action.gameCount)};
    case 'open-library': return {...state, surface: 'library', focusRegion: 'content'};
    case 'open-settings': return {...state, surface: 'settings', focusRegion: 'content'};
    case 'home': return {...state, surface: 'home', focusRegion: 'strand'};
    case 'set-space': return {...state, space: action.space, focusRegion: 'spaces'};
    case 'select-setting': return {...state, settingsIndex: Math.max(0, action.index), focusRegion: 'content'};
    case 'select-system': return {...state, systemIndex: Math.max(0, action.index), focusRegion: 'system'};
  }
}

export function selectedShellGame(games: readonly GameInstall[], state: ShellState): GameInstall | undefined {
  return games[clamp(state.selectedIndex, games.length)];
}

/** Selection is remembered while focus visits Home's top band, but its card
 * focus passes belong exclusively to the strand focus region. */
export function isShellCardFocused(state: ShellState, index: number): boolean {
  return state.surface === 'home' && state.focusRegion === 'strand' && state.selectedIndex === index;
}

/** The compact icon is never used as a wide title plate. */
export function selectedShellBackground(game: GameInstall | undefined, surface: ShellSurface): string | undefined {
  return surface === 'home' ? game?.backgroundPath : undefined;
}
