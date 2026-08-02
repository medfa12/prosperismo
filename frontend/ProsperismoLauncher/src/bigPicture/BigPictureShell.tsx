import React, {useEffect, useMemo, useReducer, useRef, useState} from 'react';
import {
  Animated,
  Easing,
  Image,
  type ImageSourcePropType,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from 'react-native';
import type {GameInstall} from '../core/models';
import {INITIAL_SHELL_STATE, reduceShellState, selectedShellGame, type ShellFocusRegion} from './shellState';
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
  {label: 'Search', symbol: '⌕'},
  {label: 'Settings', symbol: '⚙'},
  {label: 'Desktop mode', symbol: '◉'},
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
      <View style={shellStyles.focusWash} />
      <Animated.View style={[shellStyles.focusShimmer, {transform: [{translateX: shimmerTranslate}, {rotate: '-24deg'}]}]} />
    </Animated.View>
  );
}

function ExperienceTile({game, index, selectedIndex, selected, onFocus, onPress, onOptions}: {
  game: GameInstall;
  index: number;
  selectedIndex: number;
  selected: boolean;
  onFocus(): void;
  onPress(): void;
  onOptions(): void;
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

function SystemIconButton({label, symbol, focused, onFocus, onPress}: {
  label: string;
  symbol: string;
  focused: boolean;
  onFocus(): void;
  onPress(): void;
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
    <Pressable accessibilityLabel={label} accessibilityRole="button" onFocus={onFocus} onPress={onPress} style={shellStyles.systemButton}>
      <Animated.View pointerEvents="none" style={[shellStyles.systemFocusCircle, {opacity: phase}]} />
      <Animated.Text style={[shellStyles.systemGlyph, {color}]}>{symbol}</Animated.Text>
    </Pressable>
  );
}

function HomeSurface({games, selectedIndex, onSelect, onLaunch, onOptions, onLibrary}: {
  games: readonly GameInstall[];
  selectedIndex: number;
  onSelect(index: number): void;
  onLaunch(game: GameInstall): void;
  onOptions(game: GameInstall): void;
  onLibrary(): void;
}) {
  const visibleGames = games.slice(0, SHELL_METRICS.strand.maxItems);
  const selected = visibleGames[selectedIndex];
  return (
    <>
      <View style={shellStyles.strand}>
        {visibleGames.map((game, index) => <ExperienceTile game={game} index={index} key={game.gamePath} selectedIndex={selectedIndex} onFocus={() => onSelect(index)} onOptions={() => onOptions(game)} onPress={() => onLaunch(game)} selected={index === selectedIndex} />)}
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

function SettingsSurface({selectedIndex, onSelect}: {selectedIndex: number; onSelect(index: number): void}) {
  return <View style={[shellStyles.contentSurface, shellStyles.settingsSurface]}><Text style={shellStyles.surfaceTitle}>Prosperismo Settings</Text><ScrollView contentContainerStyle={shellStyles.settingsList}>{SETTINGS_CATEGORIES.map(([category, detail], index) => { const selected = index === selectedIndex; return <Pressable accessibilityRole="button" key={category} onFocus={() => onSelect(index)} onPress={() => onSelect(index)} style={shellStyles.settingsRow}>{selected && <View pointerEvents="none" style={shellStyles.settingsFocus} />}<View style={shellStyles.settingsGlyph} /><View style={shellStyles.settingsCopy}><Text style={[shellStyles.settingsText, selected && shellStyles.settingsTextFocused]}>{category}</Text><Text style={[shellStyles.settingsDetail, selected && shellStyles.settingsDetailFocused]}>{detail}</Text></View><Text style={[shellStyles.settingsChevron, selected && shellStyles.settingsTextFocused]}>›</Text></Pressable>; })}</ScrollView></View>;
}

function OptionsModal({game, onClose, onPlay}: {game: GameInstall; onClose(): void; onPlay(): void}) {
  const phase = useRef(new Animated.Value(0)).current;
  useEffect(() => { const animation = Animated.sequence([Animated.delay(50), Animated.timing(phase, {toValue: 1, duration: 250, easing: easeOutBreeze, useNativeDriver: true})]); animation.start(); return () => animation.stop(); }, [phase]);
  return <View style={shellStyles.modalLayer}><Pressable accessibilityLabel="Close options" onPress={onClose} style={shellStyles.modalScrim} /><Animated.View style={[shellStyles.optionsPanel, {opacity: phase, transform: [{translateY: phase.interpolate({inputRange: [0, 1], outputRange: [24, 0]})}]}]}><Text style={shellStyles.optionsTitle}>{game.titleName}</Text><Pressable onPress={onPlay} style={shellStyles.optionRow}><Text style={shellStyles.optionText}>Play</Text></Pressable><Pressable onPress={onClose} style={shellStyles.optionRow}><Text style={shellStyles.optionText}>Cancel</Text></Pressable></Animated.View></View>;
}

function ShellToast({message}: {message: string}) {
  const phase = useFocusPhase(true);
  return <Animated.View pointerEvents="none" style={[shellStyles.toast, {opacity: phase, transform: [{translateY: phase.interpolate({inputRange: [0, 1], outputRange: [20, 0]})}]}]}><Text style={shellStyles.toastText}>{message}</Text></Animated.View>;
}

export interface BigPictureShellProps {
  games: readonly GameInstall[];
  artwork: ImageSourcePropType;
  onDesktop(): void;
  onLaunch(game: GameInstall): void;
}

export function BigPictureShell({games, artwork, onDesktop, onLaunch}: BigPictureShellProps) {
  const [state, dispatch] = useReducer(reduceShellState, INITIAL_SHELL_STATE);
  const [now, setNow] = useState(() => new Date());
  const [optionsGame, setOptionsGame] = useState<GameInstall>();
  const [toast, setToast] = useState<string>();
  const backgroundOpacity = useRef(new Animated.Value(0.18)).current;
  const {width, height} = useWindowDimensions();
  const scale = Math.min(width / SHELL_METRICS.canvas.width, height / SHELL_METRICS.canvas.height);
  const selected = selectedShellGame(games, state);
  const shellGames = useMemo(() => games.slice(0, SHELL_METRICS.strand.maxItems), [games]);
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 30000); return () => clearInterval(timer); }, []);
  useEffect(() => { Animated.timing(backgroundOpacity, {toValue: state.surface === 'home' ? 0.18 : 0.1, duration: 300, easing: easeOutBreeze, useNativeDriver: true}).start(); }, [backgroundOpacity, state.surface]);
  useEffect(() => { if (!toast) { return undefined; } const timer = setTimeout(() => setToast(undefined), 2400); return () => clearTimeout(timer); }, [toast]);
  const focus = (region: ShellFocusRegion) => dispatch({type: 'focus', region});
  const launch = (game: GameInstall) => { setOptionsGame(undefined); setToast(`Launching ${game.titleName}`); onLaunch(game); };
  const handleKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (key === 'ArrowUp') { if (state.focusRegion === 'strand') { focus('spaces'); } else if (state.focusRegion === 'content') { dispatch({type: 'home'}); } event.stopPropagation?.(); return; }
    if (key === 'ArrowDown' && state.focusRegion === 'spaces') { focus('strand'); event.stopPropagation?.(); return; }
    if ((key === 'ArrowLeft' || key === 'ArrowRight') && state.focusRegion === 'strand') { dispatch({type: 'move', delta: key === 'ArrowLeft' ? -1 : 1, gameCount: shellGames.length}); event.stopPropagation?.(); return; }
    if (key === 'Escape' || key === 'GamepadB') { if (optionsGame) { setOptionsGame(undefined); } else if (state.surface !== 'home') { dispatch({type: 'home'}); } event.stopPropagation?.(); }
  };
  // React Native Windows exposes this event at runtime, while the shared RN
  // declaration used by this project does not include the Windows extension.
  const windowsKeyCapture = {onKeyDownCapture: handleKeyDown} as any;
  return <View style={shellStyles.viewport} {...windowsKeyCapture}><View style={[shellStyles.canvas, {transform: [{scale}]}]}>
    <Animated.Image source={artwork} style={[shellStyles.backgroundArtwork, {opacity: backgroundOpacity}]} /><View style={shellStyles.backgroundMat} /><View style={shellStyles.backgroundShade} />
    <View style={shellStyles.systemBand}><View style={shellStyles.spaces}>{(['games', 'media'] as const).map(space => <Pressable key={space} onFocus={() => dispatch({type: 'set-space', space})} onPress={() => dispatch({type: 'set-space', space})} style={shellStyles.spaceButton}><Text style={[shellStyles.spaceText, state.space === space && shellStyles.spaceTextActive, state.focusRegion === 'spaces' && state.space === space && shellStyles.spaceTextFocused]}>{space === 'games' ? 'Games' : 'Media'}</Text></Pressable>)}</View><View style={shellStyles.systemActions}>{SYSTEM_ACTIONS.map((action, index) => <SystemIconButton key={action.label} {...action} focused={state.focusRegion === 'system' && state.systemIndex === index} onFocus={() => dispatch({type: 'select-system', index})} onPress={() => { if (index === 1) { dispatch({type: 'open-settings'}); } else if (index === 2) { onDesktop(); } }} />)}<Text style={shellStyles.clock}>{formatClock(now)}</Text></View></View>
    {state.surface !== 'home' && <Pressable accessibilityRole="button" onPress={() => dispatch({type: 'home'})} style={shellStyles.backButton}><Text style={shellStyles.backText}>‹ Home</Text></Pressable>}
    {state.surface === 'home' && <HomeSurface games={shellGames} selectedIndex={Math.min(state.selectedIndex, Math.max(0, shellGames.length - 1))} onLaunch={launch} onLibrary={() => dispatch({type: 'open-library'})} onOptions={game => setOptionsGame(game)} onSelect={index => dispatch({type: 'select-game', index, gameCount: shellGames.length})} />}
    {state.surface === 'library' && <LibrarySurface games={games} onLaunch={launch} />}
    {state.surface === 'settings' && <SettingsSurface onSelect={index => dispatch({type: 'select-setting', index})} selectedIndex={state.settingsIndex} />}
    {state.surface === 'home' && selected && <Text style={shellStyles.keyGuide}>Enter Select   ·   Hold for Options</Text>}
    {optionsGame && <OptionsModal game={optionsGame} onClose={() => setOptionsGame(undefined)} onPlay={() => launch(optionsGame)} />}
    {toast && <ShellToast message={toast} />}
  </View></View>;
}

const shellStyles = StyleSheet.create({
  viewport: {flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#020408', overflow: 'hidden'},
  canvas: {position: 'absolute', width: 1920, height: 1080, backgroundColor: '#020408'},
  backgroundArtwork: {position: 'absolute', width: 1920, height: 1080, resizeMode: 'cover'},
  backgroundMat: {position: 'absolute', width: 1920, height: 1080, backgroundColor: 'rgba(2,4,8,0.2)'},
  backgroundShade: {position: 'absolute', width: 1920, height: 1080, backgroundColor: 'rgba(2,4,8,0.32)'},
  systemBand: {height: 126, marginHorizontal: 84, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between'},
  spaces: {flexDirection: 'row', alignItems: 'center', gap: 64}, spaceButton: {paddingVertical: 8},
  spaceText: {color: 'rgba(255,255,255,0.6)', fontSize: 28, fontWeight: '400'}, spaceTextActive: {color: '#fff', fontWeight: '700'}, spaceTextFocused: {textDecorationLine: 'underline'},
  systemActions: {flexDirection: 'row', alignItems: 'center', gap: 48}, systemButton: {width: 56, height: 56, borderRadius: 28, alignItems: 'center', justifyContent: 'center'},
  systemFocusCircle: {position: 'absolute', width: 56, height: 56, borderRadius: 28, backgroundColor: '#fff'}, systemGlyph: {fontSize: 34, includeFontPadding: false},
  clock: {marginLeft: 40, color: '#fff', fontSize: 28, minWidth: 120, textAlign: 'right'},
  strand: {position: 'absolute', left: 0, top: 0, width: 1920, height: 294}, tilePosition: {position: 'absolute', left: 0, top: 157, width: 106, height: 106, alignItems: 'center', justifyContent: 'center'},
  tile: {width: 106, height: 106, borderRadius: 16, overflow: 'hidden', backgroundColor: '#292929'}, tileImage: {width: '100%', height: '100%', resizeMode: 'cover'}, tileFallback: {flex: 1, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, tileMonogram: {fontSize: 48, color: '#fff', fontWeight: '700'},
  focusFrame: {position: 'absolute', left: -31, top: -31, width: 168, height: 168, borderRadius: SHELL_FOCUSED_TILE_RADIUS, overflow: 'hidden'}, focusLine: {position: 'absolute', inset: 0, borderWidth: 2, borderColor: 'rgba(255,255,255,0.88)', borderRadius: SHELL_FOCUSED_TILE_RADIUS}, focusWash: {position: 'absolute', inset: 2, backgroundColor: 'rgba(255,255,255,0.13)', borderRadius: SHELL_FOCUSED_TILE_RADIUS - 2}, focusShimmer: {position: 'absolute', left: 68, top: -70, width: 32, height: 308, backgroundColor: 'rgba(255,255,255,0.17)'},
  libraryShortcut: {position: 'absolute', left: 1602, top: 157, width: 106, height: 106, borderRadius: 16, backgroundColor: '#353535', alignItems: 'center', justifyContent: 'center'}, libraryShortcutGlyph: {fontSize: 44, color: '#fff'},
  experienceCaption: {position: 'absolute', top: 106, width: 560, height: 62, justifyContent: 'center'}, experienceTitle: {color: '#fff', fontSize: 30, fontWeight: '600'}, experienceMetaRow: {flexDirection: 'row', alignItems: 'center', marginTop: 8}, experienceMeta: {color: 'rgba(255,255,255,0.7)', fontSize: 18}, metaDivider: {width: 2, height: 22, marginHorizontal: 12, backgroundColor: 'rgba(255,255,255,0.25)'},
  backButton: {position: 'absolute', left: 84, top: 142, zIndex: 3, padding: 16}, backText: {color: '#fff', fontSize: 22}, contentSurface: {position: 'absolute', left: 172, top: 190, width: 1576, height: 820}, surfaceTitle: {color: '#fff', fontSize: 44, fontWeight: '600', marginBottom: 32},
  libraryGrid: {flexDirection: 'row', flexWrap: 'wrap', gap: 32, paddingBottom: 90}, libraryTile: {width: 370, marginBottom: 20}, libraryArt: {height: 220, borderRadius: 16, backgroundColor: '#292929', alignItems: 'center', justifyContent: 'center', resizeMode: 'cover'}, libraryMonogram: {color: '#fff', fontSize: 76, fontWeight: '700'}, libraryTitle: {color: '#fff', fontSize: 20, marginTop: 12},
  settingsSurface: {width: 1200}, settingsList: {paddingBottom: 90}, settingsRow: {height: 88, borderRadius: 16, paddingHorizontal: 20, flexDirection: 'row', alignItems: 'center', overflow: 'hidden'}, settingsFocus: {position: 'absolute', inset: 0, borderWidth: 2, borderColor: 'rgba(255,255,255,0.9)', backgroundColor: 'rgba(255,255,255,0.86)', borderRadius: 16}, settingsGlyph: {width: 32, height: 32, borderRadius: 16, backgroundColor: '#6d7480', marginRight: 20}, settingsCopy: {flex: 1}, settingsText: {color: '#fff', fontSize: 24}, settingsTextFocused: {color: '#333'}, settingsDetail: {marginTop: 3, color: 'rgba(255,255,255,0.7)', fontSize: 16}, settingsDetailFocused: {color: 'rgba(51,51,51,0.72)'}, settingsChevron: {color: '#fff', fontSize: 34},
  keyGuide: {position: 'absolute', right: 84, bottom: 44, color: 'rgba(255,255,255,0.7)', fontSize: 18}, modalLayer: {position: 'absolute', inset: 0, zIndex: 20, alignItems: 'center', justifyContent: 'center'}, modalScrim: {position: 'absolute', inset: 0, backgroundColor: 'rgba(0,0,0,0.8)'}, optionsPanel: {width: 652, borderRadius: 16, overflow: 'hidden', backgroundColor: '#f5f5f5', paddingTop: 28}, optionsTitle: {paddingHorizontal: 32, paddingBottom: 18, color: '#1d1d1f', fontSize: 28, fontWeight: '600'}, optionRow: {height: 72, justifyContent: 'center', paddingHorizontal: 32, borderTopWidth: 1, borderColor: 'rgba(0,0,0,0.08)'}, optionText: {color: '#1d1d1f', fontSize: 23}, toast: {position: 'absolute', left: 610, bottom: 72, minWidth: 700, paddingHorizontal: 28, height: 72, borderRadius: 16, justifyContent: 'center', backgroundColor: 'rgba(24,24,28,0.94)'}, toastText: {color: '#fff', fontSize: 21},
});
