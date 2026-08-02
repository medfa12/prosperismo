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
import type {GameInstall} from '../core/models';
import {INITIAL_SHELL_STATE, reduceShellState, selectedShellBackground, selectedShellGame} from './shellState';
import {
  SHELL_FOCUSED_TILE_RADIUS,
  SHELL_FOCUSED_TILE_SCALE,
  SHELL_METRICS,
  shellTileBaseX,
} from './shellMetrics';

const SETTINGS_CATEGORIES = [
  ['Library', 'Game folders, artwork, and sort order'],
  ['Emulation', 'System, timing, and compatibility defaults'],
  ['Graphics', 'Display, shaders, validation, and capture'],
  ['Controller and Input', 'Controller mapping and keyboard input'],
  ['Storage and Saves', 'Save data, shader cache, and storage'],
  ['Patches', 'Title patches and per-game configuration'],
  ['About Prosperismo', 'Version, diagnostics, and legal notices'],
] as const;

const SYSTEM_ACTIONS = [
  {label: 'Search', glyph: 'search'},
  {label: 'Settings', glyph: 'settings'},
  {label: 'Desktop mode', glyph: 'profile'},
] as const;

function formatClock(now: Date): string {
  return now.toLocaleTimeString([], {hour: 'numeric', minute: '2-digit'});
}

function easeOutBreeze(value: number): number {
  return 1 - Math.pow(1 - value, 4.6);
}

function useFocusPhase(focused: boolean, delay = 0): Animated.Value {
  const phase = useRef(new Animated.Value(focused ? 1 : 0)).current;
  useEffect(() => {
    const animation = focused
      ? Animated.sequence([
          Animated.delay(delay),
          Animated.timing(phase, {toValue: 1, duration: 300, easing: easeOutBreeze, useNativeDriver: true}),
        ])
      : Animated.timing(phase, {toValue: 0, duration: 300, easing: easeOutBreeze, useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [delay, focused, phase]);
  return phase;
}

/** Separate narrow line pass and translucent area pass. It is not a generic border. */
function CardFocusPass({phase}: {phase: Animated.Value}) {
  const shimmer = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    const animation = Animated.loop(Animated.timing(shimmer, {toValue: 1, duration: 5000, easing: Easing.linear, useNativeDriver: true}));
    animation.start();
    return () => animation.stop();
  }, [shimmer]);
  const shimmerTranslate = shimmer.interpolate({inputRange: [0, 1], outputRange: [-220, 220]});
  return (
    <Animated.View pointerEvents="none" style={[shellStyles.focusFrame, {opacity: phase}]}>
      <View style={shellStyles.focusLine} />
      <View style={shellStyles.focusWashClip}>
        <View style={shellStyles.focusWash} />
        <Animated.View style={[shellStyles.focusShimmer, {transform: [{translateX: shimmerTranslate}, {rotate: '-24deg'}]}]} />
      </View>
    </Animated.View>
  );
}

/**
 * The native renderer owns a narrow line pass for list/menu rows. Keep the
 * row dark: this is not a white selected-row fill.
 */
function FocusLine({active, radius = 16}: {active: boolean; radius?: number}) {
  const phase = useFocusPhase(active);
  return <Animated.View pointerEvents="none" style={[shellStyles.genericFocusFrame, {borderRadius: radius + SHELL_METRICS.focusLineOffset, opacity: phase}]}>
    <View style={[shellStyles.genericFocusLine, {borderRadius: radius + SHELL_METRICS.focusLineOffset}]} />
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
  const focusPhase = useFocusPhase(selected);
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
      {selected && <CardFocusPass phase={focusPhase} />}
    </Animated.View>
  );
}

type SystemGlyphKind = typeof SYSTEM_ACTIONS[number]['glyph'];
type FocusableUIManager = typeof UIManager & {focus(reactTag: number): void};

function SystemGlyph({kind, color}: {kind: SystemGlyphKind; color: Animated.AnimatedInterpolation<string>}) {
  if (kind === 'search') {
    return <View pointerEvents="none" style={shellStyles.searchGlyph}><Animated.View style={[shellStyles.searchLens, {borderColor: color}]} /><Animated.View style={[shellStyles.searchHandle, {backgroundColor: color}]} /></View>;
  }
  if (kind === 'settings') {
    return <View pointerEvents="none" style={shellStyles.settingsGlyphIcon}><Animated.View style={[shellStyles.settingsCore, {borderColor: color}]} />{SYSTEM_GEAR_TOOTH_STYLES.map((toothStyle, index) => <Animated.View key={index} style={[shellStyles.settingsTooth, toothStyle, {backgroundColor: color}]} />)}</View>;
  }
  return <View pointerEvents="none" style={shellStyles.profileGlyph}><Animated.View style={[shellStyles.profileFrame, {borderColor: color}]} /><Animated.View style={[shellStyles.profileSlash, {backgroundColor: color}]} /></View>;
}

function SystemIconButton({label, glyph, focused, onFocus, onPress, onRef}: {
  label: string;
  glyph: SystemGlyphKind;
  focused: boolean;
  onFocus(): void;
  onPress(): void;
  onRef(node: any): void;
}) {
  const phase = useRef(new Animated.Value(focused ? 1 : 0)).current;
  useEffect(() => {
    const animation = focused
      ? Animated.sequence([Animated.delay(100), Animated.timing(phase, {toValue: 1, duration: 500, easing: easeOutBreeze, useNativeDriver: false})])
      : Animated.timing(phase, {toValue: 0, duration: 200, easing: easeOutBreeze, useNativeDriver: false});
    animation.start();
    return () => animation.stop();
  }, [focused, phase]);
  const color = phase.interpolate({inputRange: [0, 1], outputRange: [SHELL_METRICS.colors.white, SHELL_METRICS.colors.iconInverted]});
  return (
    <Pressable ref={onRef} accessibilityLabel={label} accessibilityRole="button" onFocus={onFocus} onPress={onPress} style={shellStyles.systemButton}>
      <Animated.View pointerEvents="none" style={[shellStyles.systemFocusCircle, {opacity: phase}]} />
      <SystemGlyph kind={glyph} color={color} />
    </Pressable>
  );
}

function fileImageSource(path: string | undefined): ImageSourcePropType | undefined {
  return path ? {uri: `file:///${path.replace(/\\/g, '/')}`} : undefined;
}

/**
 * Development bridge for frames emitted by the recovered native background
 * renderer. Frames live in the user's oracle, never inside the application
 * package. A real native-frame host can replace this adapter without changing
 * the shell layering contract.
 */
function NativeFrameBackground({framePaths, visible}: {
  framePaths: readonly string[];
  visible: boolean;
}) {
  const [frameIndex, setFrameIndex] = useState(0);
  const frames = useMemo(() => framePaths.map(fileImageSource).filter((source): source is ImageSourcePropType => Boolean(source)), [framePaths]);
  useEffect(() => {
    setFrameIndex(0);
    if (!visible || frames.length < 2) {
      return;
    }
    const sequence = [...Array(frames.length).keys(), ...Array(Math.max(0, frames.length - 2)).fill(0).map((_, index) => frames.length - index - 2)];
    let sequenceIndex = 0;
    const timer = setInterval(() => {
      sequenceIndex = (sequenceIndex + 1) % sequence.length;
      setFrameIndex(sequence[sequenceIndex]);
    }, 500);
    return () => clearInterval(timer);
  }, [frames.length, visible]);
  if (!visible || frames.length === 0) {
    return null;
  }
  return <Image source={frames[Math.min(frameIndex, frames.length - 1)]} style={shellStyles.nativeFrameBackground} />;
}

/**
 * HOME selection requests the recovered Normal background transition degree:
 * 633.333ms of linear image handoff. The native slide/ripple shader path has
 * a separate renderer boundary, so this React Native bridge keeps only the
 * proven timing rather than inventing a spatial substitute.
 */
function ReactiveBackground({backgroundPath, fallback, dimmed}: {
  backgroundPath?: string;
  fallback: ImageSourcePropType;
  dimmed: boolean;
}) {
  const nextSource = fileImageSource(backgroundPath) ?? fallback;
  const nextKey = backgroundPath ?? 'shell-default';
  const [current, setCurrent] = useState({key: nextKey, source: nextSource});
  const [previous, setPrevious] = useState<ImageSourcePropType>();
  const crossFade = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    if (current.key === nextKey) {
      return;
    }
    setPrevious(current.source);
    setCurrent({key: nextKey, source: nextSource});
    crossFade.setValue(0);
    const animation = Animated.timing(crossFade, {toValue: 1, duration: SHELL_METRICS.titleBackgroundTransitionMs, easing: Easing.linear, useNativeDriver: true});
    animation.start(({finished}) => { if (finished) { setPrevious(undefined); } });
    return () => animation.stop();
  }, [crossFade, current, nextKey, nextSource]);
  return <>
    {previous && <Animated.Image source={previous} style={[shellStyles.backgroundArtwork, {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [dimmed ? 0.1 : 0.18, 0]})}]} />}
    <Animated.Image source={current.source} style={[shellStyles.backgroundArtwork, {opacity: crossFade.interpolate({inputRange: [0, 1], outputRange: [0, dimmed ? 0.1 : 0.18]})}]} />
  </>;
}

function HomeSurface({games, selectedIndex, onSelect, onLaunch, onOptions, onLibrary, strandRefs}: {
  games: readonly GameInstall[];
  selectedIndex: number;
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
          <Text style={shellStyles.libraryShortcutGlyph}>▦</Text>
        </Pressable>
      </View>
      {selected && <View style={[shellStyles.experienceCaption, {left: SHELL_METRICS.strand.left + SHELL_METRICS.strand.focusedSize + SHELL_METRICS.strand.focusedMargin}]}><Text numberOfLines={1} style={shellStyles.experienceTitle}>{selected.titleName}</Text><View style={shellStyles.experienceMetaRow}><Text style={shellStyles.experienceMeta}>{selected.titleId || 'Local title'}</Text><View style={shellStyles.metaDivider} /><Text style={shellStyles.experienceMeta}>{selected.gameVersion || 'Unknown version'}</Text></View></View>}
    </>
  );
}

function LibrarySurface({games, onLaunch}: {games: readonly GameInstall[]; onLaunch(game: GameInstall): void}) {
  return <View style={shellStyles.contentSurface}><Text style={shellStyles.surfaceTitle}>Game Library</Text><ScrollView contentContainerStyle={shellStyles.libraryGrid}>{games.map(game => <Pressable accessibilityRole="button" key={game.gamePath} onPress={() => onLaunch(game)} style={shellStyles.libraryTile}>{game.artworkPath ? <Image source={{uri: `file:///${game.artworkPath.replace(/\\/g, '/')}`}} style={shellStyles.libraryArt} /> : <View style={shellStyles.libraryArt}><Text style={shellStyles.libraryMonogram}>{game.titleName.slice(0, 1).toUpperCase()}</Text></View>}<Text numberOfLines={1} style={shellStyles.libraryTitle}>{game.titleName}</Text></Pressable>)}</ScrollView></View>;
}

function SettingsSurface({selectedIndex, onSelect, onActivate, onRef}: {
  selectedIndex: number;
  onSelect(index: number): void;
  onActivate(index: number): void;
  onRef(index: number, node: any): void;
}) {
  return <View style={[shellStyles.contentSurface, shellStyles.settingsSurface]}><Text style={shellStyles.surfaceTitle}>Prosperismo Settings</Text><ScrollView contentContainerStyle={shellStyles.settingsList}>{SETTINGS_CATEGORIES.map(([category, detail], index) => { const selected = index === selectedIndex; return <Pressable ref={node => onRef(index, node)} accessibilityRole="button" key={category} onFocus={() => onSelect(index)} onPress={() => onActivate(index)} style={shellStyles.settingsRow}><FocusLine active={selected} /><View style={shellStyles.settingsGlyph} /><View style={shellStyles.settingsCopy}><Text style={shellStyles.settingsText}>{category}</Text><Text style={shellStyles.settingsDetail}>{detail}</Text></View><Text style={shellStyles.settingsChevron}>›</Text></Pressable>; })}</ScrollView></View>;
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
  useEffect(() => { const animation = Animated.sequence([Animated.delay(50), Animated.timing(phase, {toValue: 1, duration: 250, easing: easeOutBreeze, useNativeDriver: true})]); animation.start(); return () => animation.stop(); }, [phase]);
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

export interface BigPictureShellProps {
  games: readonly GameInstall[];
  artwork: ImageSourcePropType;
  /** Local, user-owned oracle outputs; not bundled application assets. */
  nativeBackgroundFrames?: readonly string[];
  onDesktop(): void;
  onLaunch(game: GameInstall): void;
}

export function BigPictureShell({games, artwork, nativeBackgroundFrames = [], onDesktop, onLaunch}: BigPictureShellProps) {
  const [state, dispatch] = useReducer(reduceShellState, INITIAL_SHELL_STATE);
  const [now, setNow] = useState(() => new Date());
  const [optionsGame, setOptionsGame] = useState<GameInstall>();
  const [optionIndex, setOptionIndex] = useState(0);
  const [toast, setToast] = useState<string>();
  const dismissToast = useCallback(() => setToast(undefined), []);
  const spaceRefs = useRef<any[]>([]);
  const strandRefs = useRef<any[]>([]);
  const systemRefs = useRef<any[]>([]);
  const settingsRefs = useRef<any[]>([]);
  const optionRefs = useRef<any[]>([]);
  const {width, height} = useWindowDimensions();
  const scale = Math.min(width / SHELL_METRICS.canvas.width, height / SHELL_METRICS.canvas.height);
  const selected = selectedShellGame(games, state);
  const shellGames = useMemo(() => games.slice(0, SHELL_METRICS.strand.maxItems), [games]);
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 30000); return () => clearInterval(timer); }, []);
  const focusNative = (target: any) => {
    const tag = findNodeHandle(target);
    if (tag !== null) {
      (UIManager as FocusableUIManager).focus(tag);
    }
  };
  const launch = (game: GameInstall) => { setOptionsGame(undefined); setToast(`Launching ${game.titleName}`); onLaunch(game); };
  const openOptions = (game: GameInstall) => { setOptionIndex(0); setOptionsGame(game); };
  useEffect(() => {
    if (optionsGame) {
      focusNative(optionRefs.current[0]);
    }
  }, [optionsGame]);
  const handleKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (optionsGame && (key === 'ArrowUp' || key === 'GamepadDPadUp' || key === 'ArrowDown' || key === 'GamepadDPadDown')) {
      focusNative(optionRefs.current[Math.max(0, Math.min(1, optionIndex + ((key === 'ArrowUp' || key === 'GamepadDPadUp') ? -1 : 1)))]);
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowUp' || key === 'GamepadDPadUp') { if (state.focusRegion === 'strand') { focusNative(spaceRefs.current[state.space === 'games' ? 0 : 1]); } else if (state.focusRegion === 'system') { focusNative(spaceRefs.current[state.space === 'games' ? 0 : 1]); } else if (state.focusRegion === 'content' && state.surface === 'settings') { if (state.settingsIndex > 0) { focusNative(settingsRefs.current[state.settingsIndex - 1]); } else { focusNative(systemRefs.current[1]); } } else if (state.focusRegion === 'content') { dispatch({type: 'home'}); focusNative(strandRefs.current[state.selectedIndex]); } event.stopPropagation?.(); return; }
    if (key === 'ArrowDown' || key === 'GamepadDPadDown') { if (state.focusRegion === 'spaces' || state.focusRegion === 'system') { focusNative(strandRefs.current[state.selectedIndex]); event.stopPropagation?.(); return; } if (state.focusRegion === 'content' && state.surface === 'settings' && state.settingsIndex < SETTINGS_CATEGORIES.length - 1) { focusNative(settingsRefs.current[state.settingsIndex + 1]); event.stopPropagation?.(); return; } }
    if ((key === 'ArrowLeft' || key === 'ArrowRight') && state.focusRegion === 'strand') { const next = Math.max(0, Math.min(shellGames.length - 1, state.selectedIndex + (key === 'ArrowLeft' ? -1 : 1))); focusNative(strandRefs.current[next]); event.stopPropagation?.(); return; }
    if ((key === 'ArrowLeft' || key === 'ArrowRight') && state.focusRegion === 'system') { const next = Math.max(0, Math.min(SYSTEM_ACTIONS.length - 1, state.systemIndex + (key === 'ArrowLeft' ? -1 : 1))); focusNative(systemRefs.current[next]); event.stopPropagation?.(); return; }
    if ((key === 'ArrowLeft' || key === 'ArrowRight') && state.focusRegion === 'spaces') { const next = state.space === 'games' ? 1 : 0; focusNative(spaceRefs.current[next]); event.stopPropagation?.(); return; }
    if (key === 'Escape' || key === 'GamepadB') { if (optionsGame) { setOptionsGame(undefined); } else if (state.surface !== 'home') { dispatch({type: 'home'}); } event.stopPropagation?.(); }
  };
  // React Native Windows exposes this event at runtime, while the shared RN
  // declaration used by this project does not include the Windows extension.
  const windowsKeyCapture = {onKeyDownCapture: handleKeyDown} as any;
  return <View style={shellStyles.viewport} {...windowsKeyCapture}><View style={[shellStyles.canvas, {transform: [{scale}]}]}>
    <NativeFrameBackground framePaths={nativeBackgroundFrames} visible={state.surface === 'home'} /><ReactiveBackground backgroundPath={selectedShellBackground(selected, state.surface)} dimmed={state.surface !== 'home'} fallback={artwork} /><View style={shellStyles.backgroundMat} /><View style={shellStyles.backgroundShade} />
    <View style={shellStyles.systemBand}><View style={shellStyles.spaces}>{(['games', 'media'] as const).map((space, index) => <Pressable ref={node => { spaceRefs.current[index] = node; }} key={space} onFocus={() => dispatch({type: 'set-space', space})} onPress={() => dispatch({type: 'set-space', space})} style={shellStyles.spaceButton}><Text style={[shellStyles.spaceText, state.space === space && shellStyles.spaceTextActive, state.focusRegion === 'spaces' && state.space === space && shellStyles.spaceTextFocused]}>{space === 'games' ? 'Games' : 'Media'}</Text></Pressable>)}</View><View style={shellStyles.systemActions}>{SYSTEM_ACTIONS.map((action, index) => <SystemIconButton key={action.label} {...action} focused={state.focusRegion === 'system' && state.systemIndex === index} onRef={node => { systemRefs.current[index] = node; }} onFocus={() => dispatch({type: 'select-system', index})} onPress={() => { if (index === 1) { dispatch({type: 'open-settings'}); } else if (index === 2) { onDesktop(); } }} />)}<Text style={shellStyles.clock}>{formatClock(now)}</Text></View></View>
    {state.surface !== 'home' && <Pressable accessibilityRole="button" onPress={() => dispatch({type: 'home'})} style={shellStyles.backButton}><Text style={shellStyles.backText}>‹ Home</Text></Pressable>}
    {state.surface === 'home' && <HomeSurface games={shellGames} selectedIndex={Math.min(state.selectedIndex, Math.max(0, shellGames.length - 1))} strandRefs={strandRefs} onLaunch={launch} onLibrary={() => dispatch({type: 'open-library'})} onOptions={openOptions} onSelect={index => dispatch({type: 'select-game', index, gameCount: shellGames.length})} />}
    {state.surface === 'library' && <LibrarySurface games={games} onLaunch={launch} />}
    {state.surface === 'settings' && <SettingsSurface onRef={(index, node) => { settingsRefs.current[index] = node; }} onActivate={index => setToast(`${SETTINGS_CATEGORIES[index][0]} is available in the desktop launcher`)} onSelect={index => dispatch({type: 'select-setting', index})} selectedIndex={state.settingsIndex} />}
    {state.surface === 'home' && selected && <Text style={shellStyles.keyGuide}>Enter Select   ·   Hold for Options</Text>}
    {optionsGame && <OptionsModal game={optionsGame} onRef={(index, node) => { optionRefs.current[index] = node; }} onSelect={setOptionIndex} selectedIndex={optionIndex} onClose={() => setOptionsGame(undefined)} onPlay={() => launch(optionsGame)} />}
    {toast && <ShellToast message={toast} onClose={dismissToast} />}
  </View></View>;
}

const shellStyles = StyleSheet.create({
  viewport: {flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#020408', overflow: 'hidden'},
  canvas: {position: 'absolute', width: 1920, height: 1080, backgroundColor: '#020408'},
  nativeFrameBackground: {position: 'absolute', width: 1920, height: 1080, resizeMode: 'cover'}, backgroundArtwork: {position: 'absolute', width: 1920, height: 1080, resizeMode: 'cover'},
  backgroundMat: {position: 'absolute', width: 1920, height: 1080, backgroundColor: 'rgba(2,4,8,0.2)'},
  backgroundShade: {position: 'absolute', width: 1920, height: 1080, backgroundColor: 'rgba(2,4,8,0.32)'},
  systemBand: {height: 126, marginHorizontal: 84, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between'},
  spaces: {flexDirection: 'row', alignItems: 'center', gap: 64}, spaceButton: {paddingVertical: 8},
  spaceText: {color: 'rgba(255,255,255,0.6)', fontSize: 28, fontWeight: '400'}, spaceTextActive: {color: '#fff', fontWeight: '700'}, spaceTextFocused: {textDecorationLine: 'underline'},
  systemActions: {flexDirection: 'row', alignItems: 'center', gap: 48}, systemButton: {width: 56, height: 56, borderRadius: 28, alignItems: 'center', justifyContent: 'center'},
  systemFocusCircle: {position: 'absolute', width: 56, height: 56, borderRadius: 28, backgroundColor: '#fff'},
  searchGlyph: {width: 34, height: 34}, searchLens: {position: 'absolute', left: 3, top: 3, width: 20, height: 20, borderWidth: 4, borderRadius: 10}, searchHandle: {position: 'absolute', left: 22, top: 23, width: 13, height: 4, borderRadius: 2, transform: [{rotate: '47deg'}]},
  settingsGlyphIcon: {width: 34, height: 34, alignItems: 'center', justifyContent: 'center'}, settingsCore: {width: 15, height: 15, borderWidth: 4, borderRadius: 8}, settingsTooth: {position: 'absolute', width: 5, height: 9, borderRadius: 2}, settingsTooth0: {top: 0, left: 15}, settingsTooth1: {top: 4, right: 4, transform: [{rotate: '45deg'}]}, settingsTooth2: {top: 15, right: 0, transform: [{rotate: '90deg'}]}, settingsTooth3: {right: 4, bottom: 4, transform: [{rotate: '135deg'}]}, settingsTooth4: {bottom: 0, left: 15}, settingsTooth5: {bottom: 4, left: 4, transform: [{rotate: '45deg'}]}, settingsTooth6: {top: 15, left: 0, transform: [{rotate: '90deg'}]}, settingsTooth7: {top: 4, left: 4, transform: [{rotate: '135deg'}]},
  profileGlyph: {width: 42, height: 42, alignItems: 'center', justifyContent: 'center'}, profileFrame: {position: 'absolute', width: 42, height: 42, borderWidth: 1}, profileSlash: {width: 55, height: 1, transform: [{rotate: '45deg'}]},
  clock: {marginLeft: 40, color: '#fff', fontSize: 28, minWidth: 120, textAlign: 'right'},
  strand: {position: 'absolute', left: 0, top: 0, width: 1920, height: 294}, tilePosition: {position: 'absolute', left: 0, top: 157, width: 106, height: 106, alignItems: 'center', justifyContent: 'center'},
  tile: {width: 106, height: 106, borderRadius: 16, overflow: 'hidden', backgroundColor: '#292929'}, tileImage: {width: '100%', height: '100%', resizeMode: 'cover'}, tileFallback: {flex: 1, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, tileMonogram: {fontSize: 48, color: '#fff', fontWeight: '700'},
  focusFrame: {position: 'absolute', left: -37, top: -37, width: 180, height: 180, borderRadius: SHELL_FOCUSED_TILE_RADIUS + SHELL_METRICS.focusLineOffset, overflow: 'hidden'}, focusLine: {position: 'absolute', inset: 0, borderWidth: SHELL_METRICS.focusLineWidth, borderTopColor: 'rgba(172,188,215,0.92)', borderLeftColor: 'rgba(153,192,211,0.92)', borderRightColor: 'rgba(191,187,198,0.92)', borderBottomColor: 'rgba(214,182,172,0.92)', borderRadius: SHELL_FOCUSED_TILE_RADIUS + SHELL_METRICS.focusLineOffset}, focusWashClip: {position: 'absolute', left: SHELL_METRICS.focusLineWidth + SHELL_METRICS.focusLineOffset, top: SHELL_METRICS.focusLineWidth + SHELL_METRICS.focusLineOffset, width: 168, height: 168, overflow: 'hidden', borderRadius: SHELL_FOCUSED_TILE_RADIUS}, focusWash: {position: 'absolute', inset: 0, backgroundColor: 'rgba(255,255,255,0.13)'}, focusShimmer: {position: 'absolute', left: 68, top: -70, width: 32, height: 308, backgroundColor: 'rgba(255,255,255,0.17)'},
  libraryShortcut: {position: 'absolute', left: 1602, top: 157, width: 106, height: 106, borderRadius: 16, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, libraryShortcutGlyph: {fontSize: 44, color: '#fff'},
  experienceCaption: {position: 'absolute', top: SHELL_METRICS.strand.top + SHELL_METRICS.strand.titleTop, width: 560, height: 62, justifyContent: 'center'}, experienceTitle: {color: '#fff', fontSize: 30, fontWeight: '600'}, experienceMetaRow: {flexDirection: 'row', alignItems: 'center', marginTop: 8}, experienceMeta: {color: 'rgba(255,255,255,0.7)', fontSize: 18}, metaDivider: {width: 2, height: 22, marginHorizontal: 12, backgroundColor: 'rgba(255,255,255,0.25)'},
  backButton: {position: 'absolute', left: 84, top: 142, zIndex: 3, padding: 16}, backText: {color: '#fff', fontSize: 22}, contentSurface: {position: 'absolute', left: 172, top: 190, width: 1576, height: 820}, surfaceTitle: {color: '#fff', fontSize: 44, fontWeight: '600', marginBottom: 32},
  libraryGrid: {flexDirection: 'row', flexWrap: 'wrap', gap: 32, paddingBottom: 90}, libraryTile: {width: 370, marginBottom: 20}, libraryArt: {height: 220, borderRadius: 16, backgroundColor: '#292929', alignItems: 'center', justifyContent: 'center', resizeMode: 'cover'}, libraryMonogram: {color: '#fff', fontSize: 76, fontWeight: '700'}, libraryTitle: {color: '#fff', fontSize: 20, marginTop: 12},
  genericFocusFrame: {position: 'absolute', left: -SHELL_METRICS.focusLineOffset, top: -SHELL_METRICS.focusLineOffset, right: -SHELL_METRICS.focusLineOffset, bottom: -SHELL_METRICS.focusLineOffset, zIndex: 1}, genericFocusLine: {position: 'absolute', inset: 0, borderWidth: SHELL_METRICS.focusLineWidth, borderTopColor: 'rgba(172,188,215,0.92)', borderLeftColor: 'rgba(153,192,211,0.92)', borderRightColor: 'rgba(191,187,198,0.92)', borderBottomColor: 'rgba(214,182,172,0.92)'},
  settingsSurface: {width: 1200}, settingsList: {padding: SHELL_METRICS.focusLineOffset, paddingBottom: 90}, settingsRow: {height: 88, borderRadius: 16, paddingHorizontal: 20, flexDirection: 'row', alignItems: 'center', marginBottom: 6}, settingsGlyph: {width: 32, height: 32, borderRadius: 16, backgroundColor: '#6d7480', marginRight: 20}, settingsCopy: {flex: 1}, settingsText: {color: '#fff', fontSize: 24}, settingsDetail: {marginTop: 3, color: 'rgba(255,255,255,0.7)', fontSize: 16}, settingsChevron: {color: '#fff', fontSize: 34},
  keyGuide: {position: 'absolute', right: 84, bottom: 44, color: 'rgba(255,255,255,0.7)', fontSize: 18}, modalLayer: {position: 'absolute', inset: 0, zIndex: 20}, optionsDismissArea: {position: 'absolute', inset: 0}, optionsPanel: {position: 'absolute', left: 634, bottom: 190, width: 652, minHeight: 216, borderRadius: 16, overflow: 'visible', backgroundColor: '#080A0F', paddingBottom: 8}, optionsTitle: {paddingHorizontal: 32, paddingTop: 20, paddingBottom: 10, color: 'rgba(255,255,255,0.7)', fontSize: 18, fontWeight: '400'}, optionRow: {minHeight: 98, justifyContent: 'center', paddingHorizontal: 32, borderTopWidth: 1, borderColor: 'rgba(255,255,255,0.1)'}, optionText: {color: '#fff', fontSize: 24}, toast: {position: 'absolute', alignSelf: 'center', bottom: 0, minWidth: 80, maxWidth: 652, minHeight: 72, paddingLeft: 20, paddingRight: 24, paddingVertical: 16, borderRadius: 20, flexDirection: 'row', alignItems: 'center', backgroundColor: 'rgba(255,255,255,0.04)'}, toastIcon: {width: 40, height: 40, marginRight: 16, borderRadius: 20, alignItems: 'center', justifyContent: 'center', backgroundColor: 'rgba(255,255,255,0.08)'}, toastIconMark: {width: 12, height: 12, borderRadius: 6, backgroundColor: '#fff'}, toastText: {flexShrink: 1, color: '#fff', fontSize: 18, lineHeight: 22},
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
