/**
 * Values recovered from the HOME firmware bundle and native PUI controls.
 * This module intentionally contains only settled measurements; see
 * docs/sony-shell/ps5-rn-layout.md for the evidence locators.
 */
export const SHELL_METRICS = {
  canvas: {width: 1920, height: 1080},
  systemBandHeight: 126,
  systemInset: 84,
  systemIconSize: 56,
  systemIconPitch: 104,
  clockMarginLeft: 88,
  strand: {
    left: 172,
    top: 126,
    height: 168,
    itemSize: 106,
    focusedSize: 168,
    itemMargin: 8,
    focusedMargin: 16,
    maxItems: 11,
    radius: 16,
    titleTop: 106,
  },
  contentWidth: 1576,
  gridItemMargin: 20,
  // FocusRenderManager defaults: 3px line + 3px exterior offset. The 8px
  // control-centre constant is a different control family, not the card line.
  focusLineWidth: 3,
  focusLineOffset: 3,
  focusInset: 3,
  panelRadius: 16,
  colors: {
    darkGrey: '#353535',
    grey: '#292929',
    blank: 'rgba(255,255,255,0.05)',
    white: '#FFFFFF',
    iconInverted: '#333333',
    obscure: 'rgba(13,13,13,0.6)',
    secondaryText: 'rgba(255,255,255,0.7)',
    weakDivider: 'rgba(255,255,255,0.1)',
    modalScrim: 'rgba(0,0,0,0.8)',
    settingsBasemat: '#020408',
  },
  strandSpring: {
    stiffness: 400,
    damping: 50,
    mass: 0.2,
    overshootClamping: true,
  },
} as const;

export const SHELL_FOCUSED_TILE_SCALE =
  SHELL_METRICS.strand.focusedSize / SHELL_METRICS.strand.itemSize;

export const SHELL_FOCUSED_TILE_RADIUS =
  SHELL_METRICS.strand.radius * SHELL_FOCUSED_TILE_SCALE;

/**
 * The base (unscaled) art position for a strand item. The selected card's
 * base position includes the 31px transform-origin offset, so its on-screen
 * left edge is exactly 172 after the 106→168 scale is applied.
 */
export function shellTileBaseX(index: number, selectedIndex: number): number {
  const {left, itemSize, focusedSize, itemMargin, focusedMargin} = SHELL_METRICS.strand;
  const scaleOffset = (focusedSize - itemSize) / 2;
  if (index === selectedIndex) {
    return left + scaleOffset;
  }
  if (index < selectedIndex) {
    return left - itemMargin - itemSize - (selectedIndex - index - 1) * (itemSize + itemMargin);
  }
  return left + focusedSize + focusedMargin + (index - selectedIndex - 1) * (itemSize + itemMargin);
}

/** Kept as a testable relative form of the firmware strand calculation. */
export function shellTileTranslateX(index: number, selectedIndex: number): number {
  return shellTileBaseX(index, selectedIndex) - SHELL_METRICS.strand.left;
}
