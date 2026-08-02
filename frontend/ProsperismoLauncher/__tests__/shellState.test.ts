import {INITIAL_SHELL_STATE, reduceShellState} from '../src/bigPicture/shellState';
import {SHELL_FOCUSED_TILE_SCALE, shellTileBaseX} from '../src/bigPicture/shellMetrics';

describe('Sony-grounded shell state', () => {
  it('clamps strand selection to installed games', () => {
    const moved = reduceShellState(INITIAL_SHELL_STATE, {type: 'select-game', index: 12, gameCount: 4});
    expect(moved.selectedIndex).toBe(3);
  });

  it('keeps settings and home focus regions separate', () => {
    const settings = reduceShellState(INITIAL_SHELL_STATE, {type: 'open-settings'});
    expect(settings.surface).toBe('settings');
    expect(settings.focusRegion).toBe('content');
    const home = reduceShellState(settings, {type: 'home'});
    expect(home.surface).toBe('home');
    expect(home.focusRegion).toBe('strand');
  });

  it('matches the firmware strand packing constants', () => {
    expect(SHELL_FOCUSED_TILE_SCALE).toBeCloseTo(168 / 106, 8);
    expect(shellTileBaseX(2, 2)).toBeCloseTo(203, 8);
    expect(shellTileBaseX(1, 2)).toBeCloseTo(58, 8);
    expect(shellTileBaseX(3, 2)).toBeCloseTo(356, 8);
  });

  it('keeps the selected game while system focus moves independently', () => {
    const selected = reduceShellState(INITIAL_SHELL_STATE, {type: 'select-game', index: 3, gameCount: 5});
    const system = reduceShellState(selected, {type: 'select-system', index: 1});
    expect(system.selectedIndex).toBe(3);
    expect(system.focusRegion).toBe('system');
  });
});
