import React from 'react';
import {
  Image,
  type ImageSourcePropType,
  Pressable,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import type {GameInstall} from '../core/models';
import type {ShellFocusRegion, ShellSpace} from './shellState';

const DESIGN_WIDTH = 1920;
const DESIGN_HEIGHT = 1080;
const SYSTEM_HEIGHT = 126;
const CONTENT_INSET = 84;
const SELECTED_TILE_LEFT = 172;
const SELECTED_TILE_SIZE = 168;
const IDLE_TILE_SIZE = 106;
const IDLE_TILE_TOP = SYSTEM_HEIGHT + (SELECTED_TILE_SIZE - IDLE_TILE_SIZE) / 2;
const SELECTED_TILE_RADIUS = (SELECTED_TILE_SIZE / IDLE_TILE_SIZE) * 16;
const TITLE_LEFT = 356;
const TITLE_TOP = SYSTEM_HEIGHT + IDLE_TILE_SIZE;
const SYSTEM_ICON_SIZE = 56;
const SYSTEM_ICON_PITCH = 104;
const SYSTEM_ICON_COUNT = 3;
const CLOCK_MARGIN_LEFT = 88;
const CLOCK_WIDTH = 120;
const SYSTEM_GROUP_WIDTH =
  SYSTEM_ICON_COUNT * SYSTEM_ICON_SIZE
  + (SYSTEM_ICON_COUNT - 1) * (SYSTEM_ICON_PITCH - SYSTEM_ICON_SIZE)
  + CLOCK_MARGIN_LEFT
  + CLOCK_WIDTH;
const SYSTEM_GROUP_LEFT = DESIGN_WIDTH - CONTENT_INSET - SYSTEM_GROUP_WIDTH;

type SystemAction = 'search' | 'settings' | 'profile';

const SYSTEM_ACTIONS: readonly {label: string; action: SystemAction}[] = [
  {label: 'Search', action: 'search'},
  {label: 'Settings', action: 'settings'},
  {label: 'Profile', action: 'profile'},
];

export interface RecoveredHomeShellProps {
  games: readonly GameInstall[];
  focusRegion: ShellFocusRegion;
  selectedIndex: number;
  selectedSpace: ShellSpace;
  selectedSystemIndex: number;
  clock: string;
  backgroundPath?: string;
  settingsIconPath?: string;
  searchIconPath?: string;
  libraryIconPath?: string;
  genericGameIconPath?: string;
  viewportWidth: number;
  viewportHeight: number;
  onSelectGame(index: number): void;
  onLaunch(game: GameInstall): void;
  onOptions(game: GameInstall): void;
  onSelectSpace(space: ShellSpace): void;
  onSelectSystem(index: number): void;
  onActivateSystem(action: SystemAction): void;
  onOpenLibrary(): void;
  spaceRefs: React.MutableRefObject<any[]>;
  strandRefs: React.MutableRefObject<any[]>;
  systemRefs: React.MutableRefObject<any[]>;
}

function fileSource(path: string | undefined): ImageSourcePropType | undefined {
  return path ? {uri: `file:///${path.replace(/\\/g, '/')}`} : undefined;
}

function SearchGlyph({dark}: {dark: boolean}) {
  const color = dark ? '#333333' : '#FFFFFF';
  return <View style={glyphStyles.search}>
    <View style={[glyphStyles.searchLens, {borderColor: color}]} />
    <View style={[glyphStyles.searchHandle, {backgroundColor: color}]} />
  </View>;
}

function SettingsGlyph({dark}: {dark: boolean}) {
  const color = dark ? '#333333' : '#FFFFFF';
  return <View style={[glyphStyles.settingsOuter, {borderColor: color}]}>
    <View style={[glyphStyles.settingsInner, {borderColor: color}]} />
  </View>;
}

function ProfileGlyph({dark}: {dark: boolean}) {
  const color = dark ? '#333333' : '#FFFFFF';
  return <View style={[glyphStyles.profile, {borderColor: color}]}>
    <View style={[glyphStyles.profileHead, {backgroundColor: color}]} />
    <View style={[glyphStyles.profileShoulders, {borderColor: color}]} />
  </View>;
}

function SystemGlyph({action, focused, settingsIconPath, searchIconPath}: {
  action: SystemAction;
  focused: boolean;
  settingsIconPath?: string;
  searchIconPath?: string;
}) {
  if (action === 'settings' && settingsIconPath) {
    return <Image
      source={fileSource(settingsIconPath)}
      style={[glyphStyles.firmwareIcon, focused ? glyphStyles.firmwareIconDark : glyphStyles.firmwareIconLight]}
    />;
  }
  if (action === 'search' && searchIconPath) {
    return <Image
      source={fileSource(searchIconPath)}
      style={[glyphStyles.firmwareIcon, focused ? glyphStyles.firmwareIconDark : glyphStyles.firmwareIconLight]}
    />;
  }
  if (action === 'search') {
    return <SearchGlyph dark={focused} />;
  }
  if (action === 'settings') {
    return <SettingsGlyph dark={focused} />;
  }
  return <ProfileGlyph dark={focused} />;
}

/**
 * A deliberately small port of the recovered SharpEmu HOME surface. Every
 * coordinate is authored in the firmware bundle's 1920 x 1080 design space,
 * then multiplied into the available viewport. No native child surface is
 * mounted here, so the React tree remains visible while the background bridge
 * is repaired independently.
 */
export function RecoveredHomeShell({
  games,
  focusRegion,
  selectedIndex,
  selectedSpace,
  selectedSystemIndex,
  clock,
  backgroundPath,
  settingsIconPath,
  searchIconPath,
  libraryIconPath,
  genericGameIconPath,
  viewportWidth,
  viewportHeight,
  onSelectGame,
  onLaunch,
  onOptions,
  onSelectSpace,
  onSelectSystem,
  onActivateSystem,
  onOpenLibrary,
  spaceRefs,
  strandRefs,
  systemRefs,
}: RecoveredHomeShellProps) {
  const scale = Math.max(0.01, Math.min(viewportWidth / DESIGN_WIDTH, viewportHeight / DESIGN_HEIGHT));
  const s = (value: number) => value * scale;
  const stageWidth = s(DESIGN_WIDTH);
  const stageHeight = s(DESIGN_HEIGHT);
  const stageStyles = StyleSheet.create({
    stage: {width: stageWidth, height: stageHeight, backgroundColor: '#020408', overflow: 'hidden'},
    background: {position: 'absolute', left: 0, top: 0, width: stageWidth, height: stageHeight, resizeMode: 'cover', opacity: 0.18},
    topBand: {position: 'absolute', left: 0, top: 0, width: stageWidth, height: s(SYSTEM_HEIGHT)},
    spaces: {position: 'absolute', left: s(CONTENT_INSET), top: s(43), flexDirection: 'row'},
    spaceButton: {padding: s(8), marginRight: s(64)},
    spaceText: {fontFamily: 'Fira Sans', fontSize: s(28), color: 'rgba(255,255,255,0.6)'},
    spaceTextSelected: {color: '#FFFFFF', fontWeight: '700'},
    spaceTextFocused: {textDecorationLine: 'underline'},
    systemButton: {position: 'absolute', top: s(35), width: s(SYSTEM_ICON_SIZE), height: s(SYSTEM_ICON_SIZE), borderRadius: s(28), alignItems: 'center', justifyContent: 'center'},
    systemButtonFocused: {backgroundColor: '#FFFFFF'},
    clock: {position: 'absolute', left: s(SYSTEM_GROUP_LEFT + SYSTEM_ICON_COUNT * SYSTEM_ICON_PITCH - (SYSTEM_ICON_PITCH - SYSTEM_ICON_SIZE) + CLOCK_MARGIN_LEFT), top: s(43), width: s(CLOCK_WIDTH), color: '#FFFFFF', fontFamily: 'Fira Sans', fontSize: s(28), textAlign: 'right'},
    selectedTile: {position: 'absolute', left: s(SELECTED_TILE_LEFT), top: s(SYSTEM_HEIGHT), width: s(SELECTED_TILE_SIZE), height: s(SELECTED_TILE_SIZE), borderRadius: s(SELECTED_TILE_RADIUS), overflow: 'hidden', backgroundColor: '#292929'},
    idleTile: {position: 'absolute', top: s(IDLE_TILE_TOP), width: s(IDLE_TILE_SIZE), height: s(IDLE_TILE_SIZE), borderRadius: s(16), overflow: 'hidden', backgroundColor: '#292929'},
    tileImage: {width: '100%', height: '100%', resizeMode: 'cover'},
    tileFallback: {width: '100%', height: '100%', alignItems: 'center', justifyContent: 'center', backgroundColor: '#353535'},
    selectedMonogram: {fontFamily: 'Fira Sans', fontSize: s(58), fontWeight: '700', color: '#FFFFFF'},
    idleMonogram: {fontFamily: 'Fira Sans', fontSize: s(40), fontWeight: '700', color: '#FFFFFF'},
    cardFocus: {position: 'absolute', left: s(SELECTED_TILE_LEFT - 6), top: s(SYSTEM_HEIGHT - 6), width: s(SELECTED_TILE_SIZE + 12), height: s(SELECTED_TILE_SIZE + 12), borderRadius: s(SELECTED_TILE_RADIUS + 3), borderWidth: Math.max(1, s(3)), borderColor: 'rgba(255,255,255,0.92)'},
    cardWash: {position: 'absolute', left: s(SELECTED_TILE_LEFT), top: s(SYSTEM_HEIGHT), width: s(SELECTED_TILE_SIZE), height: s(SELECTED_TILE_SIZE), borderRadius: s(SELECTED_TILE_RADIUS), backgroundColor: 'rgba(255,255,255,0.10)'},
    titleBlock: {position: 'absolute', left: s(TITLE_LEFT), top: s(TITLE_TOP), width: s(560), height: s(62), justifyContent: 'center'},
    title: {fontFamily: 'Fira Sans', color: '#FFFFFF', fontSize: s(30), fontWeight: '600'},
    metadata: {fontFamily: 'Fira Sans', marginTop: s(6), color: 'rgba(255,255,255,0.7)', fontSize: s(18)},
    library: {position: 'absolute', left: s(1602), top: s(IDLE_TILE_TOP), width: s(IDLE_TILE_SIZE), height: s(IDLE_TILE_SIZE), borderRadius: s(16), alignItems: 'center', justifyContent: 'center', backgroundColor: '#353535'},
    libraryIcon: {width: s(40), height: s(32), resizeMode: 'contain', tintColor: '#FFFFFF'},
    libraryFallback: {fontFamily: 'Fira Sans', color: '#FFFFFF', fontSize: s(34)},
    emptyTitle: {position: 'absolute', left: s(172), top: s(168), color: '#FFFFFF', fontFamily: 'Fira Sans', fontSize: s(26), fontWeight: '600'},
    emptyHint: {position: 'absolute', left: s(172), top: s(208), color: 'rgba(255,255,255,0.7)', fontFamily: 'Fira Sans', fontSize: s(18)},
  });

  const visibleGames = games.slice(0, 11);
  const clampedIndex = Math.max(0, Math.min(selectedIndex, Math.max(0, visibleGames.length - 1)));
  const selectedGame = visibleGames[clampedIndex];
  const background = fileSource(backgroundPath);
  const libraryIcon = fileSource(libraryIconPath);
  const genericGameIcon = fileSource(genericGameIconPath);

  return <View style={stageStyles.stage}>
    {background && <Image source={background} style={stageStyles.background} />}

    <View style={stageStyles.topBand}>
      <View style={stageStyles.spaces}>{(['games', 'media'] as const).map((space, index) => {
        const selected = selectedSpace === space;
        const focused = focusRegion === 'spaces' && selected;
        return <Pressable
          accessibilityLabel={space === 'games' ? 'Games' : 'Media'}
          accessibilityRole="button"
          key={space}
          onFocus={() => onSelectSpace(space)}
          onPress={() => onSelectSpace(space)}
          ref={node => { spaceRefs.current[index] = node; }}
          style={stageStyles.spaceButton}>
          <Text style={[stageStyles.spaceText, selected && stageStyles.spaceTextSelected, focused && stageStyles.spaceTextFocused]}>
            {space === 'games' ? 'Games' : 'Media'}
          </Text>
        </Pressable>;
      })}</View>

      {SYSTEM_ACTIONS.map((item, index) => {
        const focused = focusRegion === 'system' && selectedSystemIndex === index;
        return <Pressable
          accessibilityLabel={item.label}
          accessibilityRole="button"
          key={item.action}
          onFocus={() => onSelectSystem(index)}
          onPress={() => onActivateSystem(item.action)}
          ref={node => { systemRefs.current[index] = node; }}
          style={[
            stageStyles.systemButton,
            {left: s(SYSTEM_GROUP_LEFT + index * SYSTEM_ICON_PITCH)},
            focused && stageStyles.systemButtonFocused,
          ]}>
          <SystemGlyph action={item.action} focused={focused} settingsIconPath={settingsIconPath} searchIconPath={searchIconPath} />
        </Pressable>;
      })}
      <Text style={stageStyles.clock}>{clock}</Text>
    </View>

    {selectedGame ? <>
      {visibleGames.map((game, index) => {
        const relativeIndex = index - clampedIndex;
        if (relativeIndex < 0) {
          return null;
        }
        const isSelected = index === clampedIndex;
        const left = isSelected
          ? s(SELECTED_TILE_LEFT)
          : s(SELECTED_TILE_LEFT + SELECTED_TILE_SIZE + 16 + (relativeIndex - 1) * (IDLE_TILE_SIZE + 8));
        return <Pressable
          accessibilityLabel={`${game.titleName}, ${index + 1} of ${visibleGames.length}`}
          accessibilityRole="button"
          key={game.gamePath}
          onFocus={() => onSelectGame(index)}
          onLongPress={() => onOptions(game)}
          onPress={() => onLaunch(game)}
          ref={node => { strandRefs.current[index] = node; }}
          style={[isSelected ? stageStyles.selectedTile : stageStyles.idleTile, !isSelected && {left}]}>
          {game.artworkPath
            ? <Image source={fileSource(game.artworkPath)} style={stageStyles.tileImage} />
            : genericGameIcon
              ? <Image source={genericGameIcon} style={stageStyles.tileImage} />
              : <View style={stageStyles.tileFallback}><Text style={isSelected ? stageStyles.selectedMonogram : stageStyles.idleMonogram}>{game.titleName.slice(0, 1).toUpperCase()}</Text></View>}
        </Pressable>;
      })}
      {focusRegion === 'strand' && <>
        <View pointerEvents="none" style={stageStyles.cardWash} />
        <View pointerEvents="none" style={stageStyles.cardFocus} />
      </>}
      <View style={stageStyles.titleBlock}>
        <Text numberOfLines={1} style={stageStyles.title}>{selectedGame.titleName}</Text>
        <Text numberOfLines={1} style={stageStyles.metadata}>
          {selectedGame.titleId || 'Local title'}  ·  {selectedGame.gameVersion || 'Unknown version'}
        </Text>
      </View>
    </> : <>
      <Text style={stageStyles.emptyTitle}>Your library is empty</Text>
      <Text style={stageStyles.emptyHint}>Add a game folder from Desktop mode, then return to Big Picture.</Text>
    </>}

    <Pressable accessibilityLabel="Game Library" accessibilityRole="button" onPress={onOpenLibrary} style={stageStyles.library}>
      {libraryIcon
        ? <Image source={libraryIcon} style={stageStyles.libraryIcon} />
        : <Text style={stageStyles.libraryFallback}>▦</Text>}
    </Pressable>
  </View>;
}

const glyphStyles = StyleSheet.create({
  firmwareIcon: {width: 36, height: 32, resizeMode: 'contain'},
  firmwareIconDark: {tintColor: '#333333'},
  firmwareIconLight: {tintColor: '#FFFFFF'},
  search: {width: 34, height: 34},
  searchLens: {position: 'absolute', left: 3, top: 3, width: 20, height: 20, borderWidth: 4, borderRadius: 10},
  searchHandle: {position: 'absolute', left: 22, top: 23, width: 13, height: 4, borderRadius: 2, transform: [{rotate: '47deg'}]},
  settingsOuter: {width: 31, height: 31, borderWidth: 4, borderRadius: 8, alignItems: 'center', justifyContent: 'center', transform: [{rotate: '45deg'}]},
  settingsInner: {width: 11, height: 11, borderWidth: 3, borderRadius: 6},
  profile: {width: 42, height: 42, borderWidth: 2, borderRadius: 21, alignItems: 'center'},
  profileHead: {width: 10, height: 10, borderRadius: 5, marginTop: 8},
  profileShoulders: {width: 24, height: 12, borderWidth: 2, borderBottomWidth: 0, borderTopLeftRadius: 12, borderTopRightRadius: 12, marginTop: 4},
});
