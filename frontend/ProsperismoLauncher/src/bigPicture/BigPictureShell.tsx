import React, {useCallback, useEffect, useMemo, useReducer, useRef, useState} from 'react';
import {
  Animated,
  Easing,
  findNodeHandle,
  Image,
  type ImageSourcePropType,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  UIManager,
  useWindowDimensions,
  View,
} from 'react-native';
import type {GameInstall, LauncherSettings} from '../core/models';
import {INITIAL_SHELL_STATE, reduceShellState, selectedShellBackground, selectedShellGame, type ShellDirection, type ShellFocusRegion} from './shellState';
import {
  SHELL_FOCUSED_TILE_RADIUS,
  SHELL_FOCUSED_TILE_SCALE,
  SHELL_METRICS,
  shellEaseOutBlast,
  shellHomeFocusTarget,
  shellTileBaseX,
} from './shellMetrics';
import {RecoveredHomeShell} from './RecoveredHomeShell';
import {SHELL_CLOCK_TEXT_STYLE, shellTextStyle} from './shellTypography';
import {
  PROSPERISMO_SETTINGS_CATEGORIES,
  ProsperismoSettingsDetail,
  ProsperismoSettingsRoot,
} from './ProsperismoSettingsSurface';
import {
  GenericAvatar,
  ProfileMenu,
  SearchSurface,
  ShellButtonPrompts,
} from './ShellUtilitySurfaces';

const SYSTEM_ACTIONS = [
  {label: 'Search', glyph: 'search'},
  {label: 'Settings', glyph: 'settings'},
  {label: 'Profile', glyph: 'profile'},
] as const;

export interface FirmwareShellIconPaths {
  settings?: string;
  library?: string;
  desktop?: string;
  search?: string;
  genericGame?: string;
}

function formatClock(now: Date): string {
  return now.toLocaleTimeString([], {hour: 'numeric', minute: '2-digit'});
}

function homeDirectionForKey(key: string | undefined): ShellDirection | undefined {
  if (key === 'ArrowLeft' || key === 'GamepadDPadLeft') { return 'left'; }
  if (key === 'ArrowRight' || key === 'GamepadDPadRight') { return 'right'; }
  if (key === 'ArrowUp' || key === 'GamepadDPadUp') { return 'up'; }
  if (key === 'ArrowDown' || key === 'GamepadDPadDown') { return 'down'; }
  return undefined;
}

function easeOutBreeze(value: number): number {
  return 1 - Math.pow(1 - value, 4.6);
}

function focusInOutCurve(value: number): number {
  return value > 0 ? 1 - Math.pow(1 - value * 0.5, 10) : 0;
}

function useFocusPhase(focused: boolean, delay = 200): Animated.Value {
  const phase = useRef(new Animated.Value(focused ? 1 : 0)).current;
  useEffect(() => {
    const animation = focused
      ? Animated.sequence([
          Animated.delay(delay),
          Animated.timing(phase, {toValue: 1, duration: 300, easing: focusInOutCurve, useNativeDriver: true}),
        ])
      : Animated.timing(phase, {toValue: 0, duration: 300, easing: focusInOutCurve, useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [delay, focused, phase]);
  return phase;
}

/** Separate narrow line pass and translucent area pass. It is not a generic border. */
function CardFocusPass() {
  const shimmer = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    // FocusRenderManager's ShimmerSpeed=1 / ShimmerFrequency=5 path is idle
    // for three seconds, then runs a two-second cosine pulse. This is only the
    // RN fallback for the unrecovered shimmer texture, not a substitute shader.
    const animation = Animated.loop(Animated.sequence([
      Animated.delay(3000),
      Animated.timing(shimmer, {toValue: 1, duration: 1000, easing: Easing.inOut(Easing.sin), useNativeDriver: true}),
      Animated.timing(shimmer, {toValue: 0, duration: 1000, easing: Easing.inOut(Easing.sin), useNativeDriver: true}),
    ]));
    animation.start();
    return () => animation.stop();
  }, [shimmer]);
  const shimmerTranslate = shimmer.interpolate({inputRange: [0, 1], outputRange: [-196, 196]});
  return (
    <View pointerEvents="none" style={shellStyles.focusFrame}>
      <View style={shellStyles.focusLine} />
      <View style={shellStyles.focusWashClip}>
        <Animated.View style={[shellStyles.focusShimmer, {opacity: shimmer, transform: [{translateX: shimmerTranslate}, {rotate: '-24deg'}]}]} />
      </View>
    </View>
  );
}

/** One FocusRenderManager-style owner travels between HOME focus targets. */
function HomeFocusOverlay({focusRegion, systemIndex, hasGames}: {
  focusRegion: ShellFocusRegion;
  systemIndex: number;
  hasGames: boolean;
}) {
  const initial = shellHomeFocusTarget('strand');
  const left = useRef(new Animated.Value(initial.x)).current;
  const top = useRef(new Animated.Value(initial.y)).current;
  const width = useRef(new Animated.Value(initial.width)).current;
  const height = useRef(new Animated.Value(initial.height)).current;
  const radius = useRef(new Animated.Value(initial.radius)).current;
  const visible = focusRegion === 'system' || (focusRegion === 'strand' && hasGames);
  const cardActive = focusRegion === 'strand' && hasGames;
  const systemActive = focusRegion === 'system';
  const opacity = useRef(new Animated.Value(visible ? 1 : 0)).current;
  const cardOpacity = useRef(new Animated.Value(cardActive ? 1 : 0)).current;
  const systemOpacity = useRef(new Animated.Value(systemActive ? 1 : 0)).current;
  useEffect(() => {
    const target = shellHomeFocusTarget(systemActive ? 'system' : 'strand', systemIndex);
    const animation = Animated.parallel([
      Animated.timing(left, {toValue: target.x, duration: 300, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(top, {toValue: target.y, duration: 300, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(width, {toValue: target.width, duration: 300, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(height, {toValue: target.height, duration: 300, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(radius, {toValue: target.radius, duration: 300, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(opacity, {toValue: visible ? 1 : 0, duration: visible ? 300 : 120, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(cardOpacity, {toValue: cardActive ? 1 : 0, duration: 120, easing: shellEaseOutBlast, useNativeDriver: false}),
      Animated.timing(systemOpacity, {toValue: systemActive ? 1 : 0, duration: 120, easing: shellEaseOutBlast, useNativeDriver: false}),
    ]);
    animation.start();
    return () => animation.stop();
  }, [cardActive, cardOpacity, hasGames, height, left, opacity, radius, systemActive, systemIndex, systemOpacity, top, visible, width]);
  return <Animated.View pointerEvents="none" style={[shellStyles.homeFocusOverlay, {left, top, width, height, borderRadius: radius, opacity}]}>
    <Animated.View style={[shellStyles.homeFocusPass, {opacity: cardOpacity}]}><CardFocusPass /></Animated.View>
    <Animated.View style={[shellStyles.homeSystemFocusPass, {opacity: systemOpacity}]} />
  </Animated.View>;
}

/**
 * The native renderer owns a narrow line pass for list/menu rows. Keep the
 * row dark: this is not a white selected-row fill. The recovered renderer
 * trims ListItem focus by 3 px at the top and 5 px at the bottom; its themed
 * colour table is not recovered, so do not invent a coloured gradient here.
 */
function FocusLine({active, radius = 16}: {active: boolean; radius?: number}) {
  const phase = useFocusPhase(active);
  return <Animated.View pointerEvents="none" style={[shellStyles.genericFocusFrame, {borderRadius: radius, opacity: phase}]}>
    <View style={[shellStyles.genericFocusLine, {borderRadius: radius}]} />
  </Animated.View>;
}

function ExperienceTile({game, index, selectedIndex, selected, onFocus, onPress, onOptions, onRef}: {
  game: GameInstall;
  index: number;
  selectedIndex: number;
  selected: boolean;
  onFocus(): void;
  onPress(): void;
  onOptions(): void;
  onRef(node: any): void;
}) {
  const baseX = shellTileBaseX(index, selectedIndex);
  const x = useRef(new Animated.Value(baseX)).current;
  const scale = useRef(new Animated.Value(selected ? SHELL_FOCUSED_TILE_SCALE : 1)).current;
  useEffect(() => {
    const animation = Animated.spring(x, {...SHELL_METRICS.strandSpring, toValue: shellTileBaseX(index, selectedIndex), useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [index, selectedIndex, x]);
  useEffect(() => {
    const animation = Animated.spring(scale, {...SHELL_METRICS.strandSpring, toValue: selected ? SHELL_FOCUSED_TILE_SCALE : 1, useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [scale, selected]);
  return (
    <Animated.View style={[shellStyles.tilePosition, {transform: [{translateX: x}]}]}>
      <Animated.View style={{transform: [{scale}]}}>
        <Pressable
          ref={onRef}
          accessibilityLabel={`${game.titleName}, ${index + 1} of ${SHELL_METRICS.strand.maxItems}`}
          accessibilityRole="button"
          onFocus={onFocus}
          onLongPress={onOptions}
          onPress={onPress}
          style={shellStyles.tile}>
          {game.artworkPath ? <Image source={{uri: `file:///${game.artworkPath.replace(/\\/g, '/')}`}} style={shellStyles.tileImage} /> : <View style={shellStyles.tileFallback}><Text style={shellStyles.tileMonogram}>{game.titleName.slice(0, 1).toUpperCase()}</Text></View>}
        </Pressable>
      </Animated.View>
    </Animated.View>
  );
}

type SystemGlyphKind = typeof SYSTEM_ACTIONS[number]['glyph'];
type FocusableUIManager = typeof UIManager & {focus?(reactTag: number): void};

function SystemGlyph({kind, color, sourcePath}: {kind: SystemGlyphKind; color: Animated.AnimatedInterpolation<string>; sourcePath?: string}) {
  if (sourcePath) {
    return <Animated.Image source={fileImageSource(sourcePath)} style={[shellStyles.systemImageGlyph, {tintColor: color}]} />;
  }
  if (kind === 'search') {
    return <View pointerEvents="none" style={shellStyles.searchGlyph}><Animated.View style={[shellStyles.searchLens, {borderColor: color}]} /><Animated.View style={[shellStyles.searchHandle, {backgroundColor: color}]} /></View>;
  }
  if (kind === 'settings') {
    return <View pointerEvents="none" style={shellStyles.settingsGlyphIcon}><Animated.View style={[shellStyles.settingsCore, {borderColor: color}]} />{SYSTEM_GEAR_TOOTH_STYLES.map((toothStyle, index) => <Animated.View key={index} style={[shellStyles.settingsTooth, toothStyle, {backgroundColor: color}]} />)}</View>;
  }
  return <GenericAvatar color={color} />;
}

function SystemIconButton({label, glyph, focused, sourcePath, onFocus, onPress, onRef}: {
  label: string;
  glyph: SystemGlyphKind;
  focused: boolean;
  sourcePath?: string;
  onFocus(): void;
  onPress(): void;
  onRef(node: any): void;
}) {
  const phase = useRef(new Animated.Value(focused ? 1 : 0)).current;
  useEffect(() => {
    const animation = focused
      ? Animated.sequence([Animated.delay(100), Animated.timing(phase, {toValue: 1, duration: 500, easing: shellEaseOutBlast, useNativeDriver: false})])
      : Animated.timing(phase, {toValue: 0, duration: 200, easing: shellEaseOutBlast, useNativeDriver: false});
    animation.start();
    return () => animation.stop();
  }, [focused, phase]);
  const color = phase.interpolate({inputRange: [0, 1], outputRange: [SHELL_METRICS.colors.white, SHELL_METRICS.colors.iconInverted]});
  return (
    <Pressable ref={onRef} accessibilityLabel={label} accessibilityRole="button" onFocus={onFocus} onPress={onPress} style={shellStyles.systemButton}>
      <SystemGlyph kind={glyph} color={color} sourcePath={sourcePath} />
    </Pressable>
  );
}

function fileImageSource(path: string | undefined): ImageSourcePropType | undefined {
  return path ? {uri: `file:///${path.replace(/\\/g, '/')}`} : undefined;
}

/**
 * HOME selection requests the recovered Normal background transition degree:
 * 633.333ms of linear image handoff. The native slide/ripple shader path has
 * a separate renderer boundary, so this React Native bridge keeps only the
 * proven timing rather than inventing a spatial substitute.
 */
function ReactiveBackground({backgroundPath, dimmed}: {
  backgroundPath?: string;
  dimmed: boolean;
}) {
  const nextSource = fileImageSource(backgroundPath);
  const nextKey = backgroundPath ?? 'none';
  const [current, setCurrent] = useState({key: nextKey, source: nextSource});
  const [previous, setPrevious] = useState<{key: string; source: ImageSourcePropType | undefined}>();
  const crossFade = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    if (current.key === nextKey) {
      return;
    }
    setPrevious(current);
    setCurrent({key: nextKey, source: nextSource});
    crossFade.setValue(0);
    const animation = Animated.timing(crossFade, {toValue: 1, duration: SHELL_METRICS.titleBackgroundTransitionMs, easing: Easing.linear, useNativeDriver: true});
    animation.start(({finished}) => { if (finished) { setPrevious(undefined); } });
    return () => animation.stop();
  }, [crossFade, current, nextKey, nextSource]);
  const targetOpacity = dimmed ? 0.1 : 0.18;
  const previousOpacity = dimmed ? 0.1 : 0.18;
  return <>
    {previous?.source && <Animated.Image source={previous.source} style={[shellStyles.backgroundArtwork, {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [previousOpacity, 0]})}]} />}
    {current.source && <Animated.Image source={current.source} style={[shellStyles.backgroundArtwork, {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [0, targetOpacity]})}]} />}
  </>;
}

function HomeSurface({games, selectedIndex, libraryIconPath, onSelect, onLaunch, onOptions, onLibrary, strandRefs}: {
  games: readonly GameInstall[];
  selectedIndex: number;
  libraryIconPath?: string;
  onSelect(index: number): void;
  onLaunch(game: GameInstall): void;
  onOptions(game: GameInstall): void;
  onLibrary(): void;
  strandRefs: React.MutableRefObject<any[]>;
}) {
  const visibleGames = games.slice(0, SHELL_METRICS.strand.maxItems);
  const selected = visibleGames[selectedIndex];
  return (
    <>
      <View style={shellStyles.strand}>
        {visibleGames.map((game, index) => <ExperienceTile game={game} index={index} key={game.gamePath} selectedIndex={selectedIndex} onRef={node => { strandRefs.current[index] = node; }} onFocus={() => onSelect(index)} onOptions={() => onOptions(game)} onPress={() => onLaunch(game)} selected={index === selectedIndex} />)}
        <Pressable accessibilityLabel="Game Library" accessibilityRole="button" onPress={onLibrary} style={shellStyles.libraryShortcut}>
          {libraryIconPath
            ? <Image source={fileImageSource(libraryIconPath)} style={shellStyles.libraryShortcutImage} />
            : <Text style={shellStyles.libraryShortcutGlyph}>▦</Text>}
        </Pressable>
      </View>
      {selected && <View style={[shellStyles.experienceCaption, {left: SHELL_METRICS.strand.left + SHELL_METRICS.strand.focusedSize + SHELL_METRICS.strand.focusedMargin}]}><Text numberOfLines={1} style={shellStyles.experienceTitle}>{selected.titleName}</Text><View style={shellStyles.experienceMetaRow}><Text style={shellStyles.experienceMeta}>{selected.titleId || 'Local title'}</Text><View style={shellStyles.metaDivider} /><Text style={shellStyles.experienceMeta}>{selected.gameVersion || 'Unknown version'}</Text></View></View>}
    </>
  );
}

function LibrarySurface({games, onLaunch}: {games: readonly GameInstall[]; onLaunch(game: GameInstall): void}) {
  return <View style={shellStyles.contentSurface}><Text style={shellStyles.surfaceTitle}>Game Library</Text><ScrollView contentContainerStyle={shellStyles.libraryGrid}>{games.map(game => <Pressable accessibilityRole="button" key={game.gamePath} onPress={() => onLaunch(game)} style={shellStyles.libraryTile}>{game.artworkPath ? <Image source={{uri: `file:///${game.artworkPath.replace(/\\/g, '/')}`}} style={shellStyles.libraryArt} /> : <View style={shellStyles.libraryArt}><Text style={shellStyles.libraryMonogram}>{game.titleName.slice(0, 1).toUpperCase()}</Text></View>}<Text numberOfLines={1} style={shellStyles.libraryTitle}>{game.titleName}</Text></Pressable>)}</ScrollView></View>;
}

function OptionsModal({game, selectedIndex, onClose, onPlay, onSelect, onRef}: {
  game: GameInstall;
  selectedIndex: number;
  onClose(): void;
  onPlay(): void;
  onSelect(index: number): void;
  onRef(index: number, node: any): void;
}) {
  const phase = useRef(new Animated.Value(0)).current;
  useEffect(() => { const animation = Animated.sequence([Animated.delay(50), Animated.timing(phase, {toValue: 1, duration: 250, easing: shellEaseOutBlast, useNativeDriver: true})]); animation.start(); return () => animation.stop(); }, [phase]);
  return <View style={shellStyles.modalLayer}><Pressable accessibilityLabel="Close options" onPress={onClose} style={shellStyles.optionsDismissArea} /><Animated.View style={[shellStyles.optionsPanel, {opacity: phase}]}><Text style={shellStyles.optionsTitle}>{game.titleName}</Text><Pressable ref={node => onRef(0, node)} onFocus={() => onSelect(0)} onPress={onPlay} style={shellStyles.optionRow}><FocusLine active={selectedIndex === 0} /><Text style={shellStyles.optionText}>Play</Text></Pressable><Pressable ref={node => onRef(1, node)} onFocus={() => onSelect(1)} onPress={onClose} style={shellStyles.optionRow}><FocusLine active={selectedIndex === 1} /><Text style={shellStyles.optionText}>Cancel</Text></Pressable></Animated.View></View>;
}

function ShellToast({message, onClose}: {message: string; onClose(): void}) {
  const phase = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    const animation = Animated.sequence([
      Animated.timing(phase, {toValue: 1, duration: 300, easing: Easing.linear, useNativeDriver: true}),
      Animated.delay(3500),
      Animated.timing(phase, {toValue: 0, duration: 200, easing: Easing.linear, useNativeDriver: true}),
    ]);
    animation.start(({finished}) => { if (finished) { onClose(); } });
    return () => animation.stop();
  }, [onClose, phase]);
  return <Animated.View pointerEvents="none" style={[shellStyles.toast, {opacity: phase}]}><View style={shellStyles.toastIcon}><View style={shellStyles.toastIconMark} /></View><Text numberOfLines={2} style={shellStyles.toastText}>{message}</Text></Animated.View>;
}

/**
 * The action-card host uses a 764 x 440 dialog with a 676px body, 44px side
 * margins, a 40px message icon, and 388px text buttons. Keep the emulator
 * error inside the controller shell rather than falling back to Desktop.
 */
function ShellDialog({title, message, onDismiss, onRef}: {title: string; message: string; onDismiss(): void; onRef(node: any): void}) {
  const phase = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    const animation = Animated.timing(phase, {toValue: 1, duration: 300, easing: easeOutBreeze, useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [phase]);
  return <View style={shellStyles.dialogLayer}>
    <Animated.View style={[shellStyles.dialogPanel, {opacity: phase}]}>
      <View style={shellStyles.dialogBody}><View style={shellStyles.dialogMessageIcon}><View style={shellStyles.dialogMessageMark} /></View><Text style={shellStyles.dialogTitle}>{title}</Text><Text style={shellStyles.dialogMessage}>{message}</Text></View>
      <Pressable ref={onRef} accessibilityLabel="OK" accessibilityRole="button" onPress={onDismiss} style={shellStyles.dialogButton}><FocusLine active /><Text style={shellStyles.dialogButtonText}>OK</Text></Pressable>
    </Animated.View>
  </View>;
}

export interface BigPictureShellProps {
  games: readonly GameInstall[];
  firmwareShellIcons?: FirmwareShellIconPaths;
  settings: LauncherSettings;
  onSaveSettings(next: LauncherSettings): void;
  onDesktop(): void;
  onLaunch(game: GameInstall): void;
  errorMessage?: string;
  onDismissError(): void;
}

export function BigPictureShell({games, firmwareShellIcons = {}, settings, onSaveSettings, onDesktop, onLaunch, errorMessage, onDismissError}: BigPictureShellProps) {
  const [state, dispatch] = useReducer(reduceShellState, INITIAL_SHELL_STATE);
  const [now, setNow] = useState(() => new Date());
  const [optionsGame, setOptionsGame] = useState<GameInstall>();
  const [optionIndex, setOptionIndex] = useState(0);
  const [settingsDetail, setSettingsDetail] = useState<number>();
  const [searchOpen, setSearchOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [toast, setToast] = useState<string>();
  const dismissToast = useCallback(() => setToast(undefined), []);
  const spaceRefs = useRef<any[]>([]);
  const strandRefs = useRef<any[]>([]);
  const systemRefs = useRef<any[]>([]);
  const settingsRefs = useRef<any[]>([]);
  const optionRefs = useRef<any[]>([]);
  const dialogRef = useRef<any>(undefined);
  const {width, height} = useWindowDimensions();
  const scale = Math.min(width / SHELL_METRICS.canvas.width, height / SHELL_METRICS.canvas.height);
  const selected = selectedShellGame(games, state);
  const shellGames = useMemo(() => games.slice(0, SHELL_METRICS.strand.maxItems), [games]);
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 30000); return () => clearInterval(timer); }, []);
  const focusNative = (target: any) => {
    if (typeof target?.focus === 'function') {
      target.focus();
      return;
    }
    const tag = findNodeHandle(target);
    const manager = UIManager as FocusableUIManager;
    if (tag !== null && typeof manager.focus === 'function') {
      manager.focus(tag);
    }
  };
  const launch = (game: GameInstall) => { setOptionsGame(undefined); setToast(`Launching ${game.titleName}`); onLaunch(game); };
  const openOptions = (game: GameInstall) => { setOptionIndex(0); setOptionsGame(game); };
  useEffect(() => {
    if (optionsGame) {
      focusNative(optionRefs.current[0]);
    }
  }, [optionsGame]);
  useEffect(() => {
    if (errorMessage) {
      focusNative(dialogRef.current);
    }
  }, [errorMessage]);
  useEffect(() => {
    if (optionsGame || errorMessage || settingsDetail !== undefined) {
      return;
    }
    if (state.surface === 'settings') {
      focusNative(settingsRefs.current[state.settingsIndex]);
      return;
    }
    if (state.surface === 'home') {
      if (state.focusRegion === 'system') {
        focusNative(systemRefs.current[Math.min(state.systemIndex, SYSTEM_ACTIONS.length - 1)]);
      } else if (state.focusRegion === 'spaces' || shellGames.length === 0) {
        focusNative(spaceRefs.current[state.space === 'games' ? 0 : 1]);
      } else {
        focusNative(strandRefs.current[Math.min(state.selectedIndex, shellGames.length - 1)]);
      }
    }
  }, [errorMessage, optionsGame, settingsDetail, shellGames.length, state.focusRegion, state.selectedIndex, state.settingsIndex, state.space, state.surface, state.systemIndex]);
  const handleKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (searchOpen || profileOpen) {
      return;
    }
    if (errorMessage) {
      if (key === 'Escape' || key === 'GamepadB') { onDismissError(); }
      event.stopPropagation?.();
      return;
    }
    if (optionsGame && (key === 'ArrowUp' || key === 'GamepadDPadUp' || key === 'ArrowDown' || key === 'GamepadDPadDown')) {
      focusNative(optionRefs.current[Math.max(0, Math.min(1, optionIndex + ((key === 'ArrowUp' || key === 'GamepadDPadUp') ? -1 : 1)))]);
      event.stopPropagation?.();
      return;
    }
    if (state.surface === 'home' && selected && (key === 'GamepadMenu' || key === 'ContextMenu' || key === 'F10')) {
      openOptions(selected);
      event.stopPropagation?.();
      return;
    }
    const homeDirection = homeDirectionForKey(key);
    if (state.surface === 'home' && homeDirection) {
      dispatch({type: 'navigate-home', direction: homeDirection, gameCount: shellGames.length, systemCount: SYSTEM_ACTIONS.length});
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowUp' || key === 'GamepadDPadUp') { if (state.focusRegion === 'content' && state.surface === 'settings' && settingsDetail === undefined) { if (state.settingsIndex > 0) { focusNative(settingsRefs.current[state.settingsIndex - 1]); } else { dispatch({type: 'home'}); focusNative(strandRefs.current[state.selectedIndex]); } } else if (state.focusRegion === 'content' && settingsDetail === undefined) { dispatch({type: 'home'}); focusNative(strandRefs.current[state.selectedIndex]); } event.stopPropagation?.(); return; }
    if (key === 'ArrowDown' || key === 'GamepadDPadDown') { if (state.focusRegion === 'content' && state.surface === 'settings' && settingsDetail === undefined && state.settingsIndex < PROSPERISMO_SETTINGS_CATEGORIES.length - 1) { focusNative(settingsRefs.current[state.settingsIndex + 1]); event.stopPropagation?.(); return; } }
    if (key === 'Escape' || key === 'GamepadB') { if (optionsGame) { setOptionsGame(undefined); } else if (settingsDetail !== undefined) { setSettingsDetail(undefined); } else if (state.surface !== 'home') { dispatch({type: 'home'}); } event.stopPropagation?.(); }
  };
  // React Native Windows exposes this event at runtime, while the shared RN
  // declaration used by this project does not include the Windows extension.
  const windowsKeyCapture = {onKeyDownCapture: handleKeyDown} as any;
  if (state.surface === 'home' && !searchOpen && !profileOpen && !optionsGame && !errorMessage) {
    return <View style={shellStyles.viewport} {...windowsKeyCapture}>
      <RecoveredHomeShell
        backgroundPath={selectedShellBackground(selected, state.surface)}
        clock={formatClock(now)}
        focusRegion={state.focusRegion}
        games={shellGames}
        libraryIconPath={firmwareShellIcons.library}
        genericGameIconPath={firmwareShellIcons.genericGame}
        onActivateSystem={action => {
          if (action === 'search') {
            setSearchOpen(true);
          } else if (action === 'settings') {
            dispatch({type: 'open-settings'});
          } else {
            setProfileOpen(true);
          }
        }}
        onLaunch={launch}
        onOpenLibrary={() => dispatch({type: 'open-library'})}
        onOptions={openOptions}
        onSelectGame={index => dispatch({type: 'select-game', index, gameCount: shellGames.length})}
        onSelectSpace={space => dispatch({type: 'set-space', space})}
        onSelectSystem={index => dispatch({type: 'select-system', index})}
        selectedIndex={Math.min(state.selectedIndex, Math.max(0, shellGames.length - 1))}
        selectedSpace={state.space}
        selectedSystemIndex={state.systemIndex}
        settingsIconPath={firmwareShellIcons.settings}
        searchIconPath={firmwareShellIcons.search}
        spaceRefs={spaceRefs}
        strandRefs={strandRefs}
        systemRefs={systemRefs}
        viewportHeight={height}
        viewportWidth={width}
      />
    </View>;
  }
  return <View style={shellStyles.viewport} {...windowsKeyCapture}><View style={[shellStyles.canvas, {transform: [{scale}]}]}>
    <ReactiveBackground backgroundPath={selectedShellBackground(selected, state.surface)} dimmed={state.surface !== 'home'} />
    {state.surface !== 'settings' && <View style={shellStyles.systemBand}><View style={shellStyles.spaces}>{(['games', 'media'] as const).map((space, index) => <Pressable ref={node => { spaceRefs.current[index] = node; }} key={space} onFocus={() => dispatch({type: 'set-space', space})} onPress={() => dispatch({type: 'set-space', space})} style={shellStyles.spaceButton}><Text style={[shellStyles.spaceText, state.space === space && shellStyles.spaceTextActive, state.focusRegion === 'spaces' && state.space === space && shellStyles.spaceTextFocused]}>{space === 'games' ? 'Games' : 'Media'}</Text></Pressable>)}</View><View style={shellStyles.systemActions}>{SYSTEM_ACTIONS.map((action, index) => <SystemIconButton key={action.label} {...action} focused={state.focusRegion === 'system' && state.systemIndex === index} sourcePath={action.glyph === 'settings' ? firmwareShellIcons.settings : undefined} onRef={node => { systemRefs.current[index] = node; }} onFocus={() => dispatch({type: 'select-system', index})} onPress={() => { if (index === 0) { setSearchOpen(true); } else if (index === 1) { dispatch({type: 'open-settings'}); } else { setProfileOpen(true); } }} />)}<Text style={shellStyles.clock}>{formatClock(now)}</Text></View></View>}
    {state.surface !== 'home' && state.surface !== 'settings' && settingsDetail === undefined && <Pressable accessibilityRole="button" onPress={() => dispatch({type: 'home'})} style={shellStyles.backButton}><Text style={shellStyles.backText}>‹ Home</Text></Pressable>}
    {state.surface === 'home' && <HomeSurface games={shellGames} libraryIconPath={firmwareShellIcons.library} selectedIndex={Math.min(state.selectedIndex, Math.max(0, shellGames.length - 1))} strandRefs={strandRefs} onLaunch={launch} onLibrary={() => dispatch({type: 'open-library'})} onOptions={openOptions} onSelect={index => dispatch({type: 'select-game', index, gameCount: shellGames.length})} />}
    {state.surface === 'home' && <HomeFocusOverlay focusRegion={state.focusRegion} hasGames={shellGames.length > 0} systemIndex={state.systemIndex} />}
    {state.surface === 'library' && <LibrarySurface games={games} onLaunch={launch} />}
    {state.surface === 'settings' && settingsDetail === undefined && <ProsperismoSettingsRoot onRef={(index, node) => { settingsRefs.current[index] = node; }} onActivate={setSettingsDetail} onSelect={index => dispatch({type: 'select-setting', index})} selectedIndex={state.settingsIndex} />}
    {state.surface === 'settings' && settingsDetail !== undefined && <ProsperismoSettingsDetail categoryIndex={settingsDetail} onBack={() => setSettingsDetail(undefined)} onSave={onSaveSettings} settings={settings} />}
    {!searchOpen && !profileOpen && !errorMessage && <ShellButtonPrompts prompts={state.surface === 'home' && selected ? [{kind: 'confirm', label: 'Select'}, {kind: 'options', label: 'Options'}] : [{kind: 'confirm', label: 'Select'}, {kind: 'back', label: 'Back'}]} />}
    {optionsGame && <OptionsModal game={optionsGame} onRef={(index, node) => { optionRefs.current[index] = node; }} onSelect={setOptionIndex} selectedIndex={optionIndex} onClose={() => setOptionsGame(undefined)} onPlay={() => launch(optionsGame)} />}
    {searchOpen && <SearchSurface games={games} onClose={() => { setSearchOpen(false); focusNative(systemRefs.current[0]); }} onLaunch={game => { setSearchOpen(false); launch(game); }} />}
    {profileOpen && <ProfileMenu onClose={() => { setProfileOpen(false); focusNative(systemRefs.current[2]); }} onDesktop={() => { setProfileOpen(false); onDesktop(); }} />}
    {errorMessage && <ShellDialog title="Unable to start game" message={errorMessage} onDismiss={onDismissError} onRef={node => { dialogRef.current = node; }} />}
    {toast && <ShellToast message={toast} onClose={dismissToast} />}
  </View></View>;
}

const shellStyles = StyleSheet.create({
  viewport: {flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#020408', overflow: 'hidden'},
  canvas: {position: 'absolute', width: 1920, height: 1080, backgroundColor: '#020408'},
  nativeFrameBackground: {position: 'absolute', width: 1920, height: 1080, resizeMode: 'cover'}, backgroundArtwork: {position: 'absolute', width: 1920, height: 1080, resizeMode: 'cover'},
  systemBand: {height: 126, marginHorizontal: 84, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', zIndex: 3},
  spaces: {flexDirection: 'row', alignItems: 'center', gap: 64}, spaceButton: {paddingVertical: 8},
  spaceText: {color: 'rgba(255,255,255,0.6)', ...shellTextStyle('SizeLarge')}, spaceTextActive: {color: '#fff', fontWeight: '700'}, spaceTextFocused: {textDecorationLine: 'underline'},
  systemActions: {flexDirection: 'row', alignItems: 'center', gap: 48}, systemButton: {width: 56, height: 56, borderRadius: 28, alignItems: 'center', justifyContent: 'center'},
  systemImageGlyph: {width: 36, height: 32, resizeMode: 'contain'},
  searchGlyph: {width: 34, height: 34}, searchLens: {position: 'absolute', left: 3, top: 3, width: 20, height: 20, borderWidth: 4, borderRadius: 10}, searchHandle: {position: 'absolute', left: 22, top: 23, width: 13, height: 4, borderRadius: 2, transform: [{rotate: '47deg'}]},
  settingsGlyphIcon: {width: 34, height: 34, alignItems: 'center', justifyContent: 'center'}, settingsCore: {width: 15, height: 15, borderWidth: 4, borderRadius: 8}, settingsTooth: {position: 'absolute', width: 5, height: 9, borderRadius: 2}, settingsTooth0: {top: 0, left: 15}, settingsTooth1: {top: 4, right: 4, transform: [{rotate: '45deg'}]}, settingsTooth2: {top: 15, right: 0, transform: [{rotate: '90deg'}]}, settingsTooth3: {right: 4, bottom: 4, transform: [{rotate: '135deg'}]}, settingsTooth4: {bottom: 0, left: 15}, settingsTooth5: {bottom: 4, left: 4, transform: [{rotate: '45deg'}]}, settingsTooth6: {top: 15, left: 0, transform: [{rotate: '90deg'}]}, settingsTooth7: {top: 4, left: 4, transform: [{rotate: '135deg'}]},
  profileGlyph: {width: 42, height: 42, alignItems: 'center', justifyContent: 'center'}, profileFrame: {position: 'absolute', width: 42, height: 42, borderWidth: 1}, profileSlash: {width: 55, height: 1, transform: [{rotate: '45deg'}]},
  clock: {marginLeft: 40, color: '#fff', minWidth: 120, ...SHELL_CLOCK_TEXT_STYLE},
  strand: {position: 'absolute', left: 0, top: 0, width: 1920, height: 294}, tilePosition: {position: 'absolute', left: 0, top: 157, width: 106, height: 106, alignItems: 'center', justifyContent: 'center'},
  tile: {width: 106, height: 106, borderRadius: 16, overflow: 'hidden', backgroundColor: '#292929'}, tileImage: {width: '100%', height: '100%', resizeMode: 'cover'}, tileFallback: {flex: 1, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, tileMonogram: {color: '#fff', ...shellTextStyle('SizeXLarge', '700')},
  homeFocusOverlay: {position: 'absolute', zIndex: 2, overflow: 'hidden'}, homeFocusPass: {position: 'absolute', inset: 0}, homeSystemFocusPass: {position: 'absolute', inset: 0, backgroundColor: '#fff'},
  focusFrame: {position: 'absolute', inset: 0, borderRadius: SHELL_FOCUSED_TILE_RADIUS + SHELL_METRICS.focusLineOffset, overflow: 'hidden'}, focusLine: {position: 'absolute', inset: 0, borderWidth: SHELL_METRICS.focusLineWidth, borderTopColor: 'rgba(172,188,215,0.92)', borderLeftColor: 'rgba(153,192,211,0.92)', borderRightColor: 'rgba(191,187,198,0.92)', borderBottomColor: 'rgba(214,182,172,0.92)', borderRadius: SHELL_FOCUSED_TILE_RADIUS + SHELL_METRICS.focusLineOffset}, focusWashClip: {position: 'absolute', left: SHELL_METRICS.focusLineWidth + SHELL_METRICS.focusLineOffset, top: SHELL_METRICS.focusLineWidth + SHELL_METRICS.focusLineOffset, width: 168, height: 168, overflow: 'hidden', borderRadius: SHELL_FOCUSED_TILE_RADIUS}, focusShimmer: {position: 'absolute', left: -44, top: -70, width: 256, height: 308, backgroundColor: 'rgba(255,255,255,0.13)'},
  libraryShortcut: {position: 'absolute', left: 1602, top: 157, width: 106, height: 106, borderRadius: 16, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, libraryShortcutGlyph: {color: '#fff', ...shellTextStyle('SizeXLarge')}, libraryShortcutImage: {width: 40, height: 32, resizeMode: 'contain'},
  experienceCaption: {position: 'absolute', top: SHELL_METRICS.strand.top + SHELL_METRICS.strand.titleTop, width: 560, height: 62, justifyContent: 'center'}, experienceTitle: {color: '#fff', ...shellTextStyle('SizeNormal', '600')}, experienceMetaRow: {flexDirection: 'row', alignItems: 'center', marginTop: 8}, experienceMeta: {color: 'rgba(255,255,255,0.7)', ...shellTextStyle('Size3XSmall')}, metaDivider: {width: 2, height: 22, marginHorizontal: 12, backgroundColor: 'rgba(255,255,255,0.25)'},
  backButton: {position: 'absolute', left: 84, top: 142, zIndex: 3, padding: 16}, backText: {color: '#fff', ...shellTextStyle('Size2XSmall')}, contentSurface: {position: 'absolute', left: 172, top: 190, width: 1576, height: 820}, surfaceTitle: {color: '#fff', marginBottom: 32, ...shellTextStyle('SizeXLarge', '600')},
  libraryGrid: {flexDirection: 'row', flexWrap: 'wrap', gap: 32, paddingBottom: 90}, libraryTile: {width: 370, marginBottom: 20}, libraryArt: {height: 220, borderRadius: 16, backgroundColor: '#292929', alignItems: 'center', justifyContent: 'center', resizeMode: 'cover'}, libraryMonogram: {color: '#fff', ...shellTextStyle('Size3XLarge', '700')}, libraryTitle: {color: '#fff', marginTop: 12, ...shellTextStyle('Size2XSmall')},
  genericFocusFrame: {position: 'absolute', left: 0, top: 3, right: 0, bottom: 5, zIndex: 1}, genericFocusLine: {position: 'absolute', inset: 0, borderWidth: SHELL_METRICS.focusLineWidth, borderColor: 'rgba(255,255,255,0.92)'},
  modalLayer: {position: 'absolute', inset: 0, zIndex: 20}, optionsDismissArea: {position: 'absolute', inset: 0}, optionsPanel: {position: 'absolute', left: 634, bottom: 190, width: 652, minHeight: 216, borderRadius: 16, overflow: 'visible', backgroundColor: '#080A0F', paddingBottom: 8}, optionsTitle: {paddingHorizontal: 32, paddingTop: 20, paddingBottom: 10, color: 'rgba(255,255,255,0.7)', ...shellTextStyle('Size3XSmall')}, optionRow: {minHeight: 98, justifyContent: 'center', paddingHorizontal: 32, borderTopWidth: 1, borderColor: 'rgba(255,255,255,0.1)'}, optionText: {color: '#fff', ...shellTextStyle('SizeXSmall')}, dialogLayer: {position: 'absolute', inset: 0, zIndex: 30, alignItems: 'center', justifyContent: 'center'}, dialogPanel: {width: 764, height: 440, borderRadius: 16, backgroundColor: '#080A0F', overflow: 'hidden', justifyContent: 'space-between', alignItems: 'center', paddingTop: 58, paddingBottom: 40}, dialogBody: {width: 676, alignItems: 'center'}, dialogMessageIcon: {width: 40, height: 40, borderRadius: 20, borderWidth: 2, borderColor: '#fff', alignItems: 'center', justifyContent: 'center', marginBottom: 20}, dialogMessageMark: {width: 3, height: 15, borderRadius: 2, backgroundColor: '#fff'}, dialogTitle: {color: '#fff', textAlign: 'center', marginBottom: 20, ...shellTextStyle('SizeNormal', '600')}, dialogMessage: {color: 'rgba(255,255,255,0.7)', lineHeight: 28, textAlign: 'center', ...shellTextStyle('Size2XSmall')}, dialogButton: {width: 388, height: 72, borderRadius: 16, alignItems: 'center', justifyContent: 'center'}, dialogButtonText: {color: '#fff', ...shellTextStyle('SizeXSmall')}, toast: {position: 'absolute', alignSelf: 'center', bottom: 0, minWidth: 80, maxWidth: 652, minHeight: 72, paddingLeft: 20, paddingRight: 24, paddingVertical: 16, borderRadius: 20, flexDirection: 'row', alignItems: 'center', backgroundColor: 'rgba(255,255,255,0.04)'}, toastIcon: {width: 40, height: 40, marginRight: 16, borderRadius: 20, alignItems: 'center', justifyContent: 'center', backgroundColor: 'rgba(255,255,255,0.08)'}, toastIconMark: {width: 12, height: 12, borderRadius: 6, backgroundColor: '#fff'}, toastText: {flexShrink: 1, color: '#fff', lineHeight: 22, ...shellTextStyle('Size3XSmall')},
});

const SYSTEM_GEAR_TOOTH_STYLES = [
  shellStyles.settingsTooth0,
  shellStyles.settingsTooth1,
  shellStyles.settingsTooth2,
  shellStyles.settingsTooth3,
  shellStyles.settingsTooth4,
  shellStyles.settingsTooth5,
  shellStyles.settingsTooth6,
  shellStyles.settingsTooth7,
] as const;
