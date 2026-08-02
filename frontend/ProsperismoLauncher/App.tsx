import React, {useCallback, useEffect, useMemo, useState} from 'react';
import {
  ActivityIndicator,
  Image,
  Pressable,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import type {GameInstall, LauncherSettings} from './src/core/models';
import {DEFAULT_LAUNCHER_SETTINGS} from './src/core/models';
import {launchGame} from './src/core/launcher';
import {scanGameDirectories} from './src/core/scanner';
import {loadSettings, saveSettings} from './src/core/settings';
import {
  hasNativeProsperismoHost,
  prosperismoHost,
} from './src/native/ProsperismoHost';

const brandArtwork = {
  desktopDark: require('../../assets/branding/ps-iOS-ClearDark-1024.png'),
  desktopLight: require('../../assets/branding/ps-iOS-ClearLight-1024.png'),
  bigPicture: require('../../assets/branding/ps-iOS-Dark-1024.png'),
  bigPictureDefault: require('../../assets/branding/ps-iOS-Default-1024.png'),
};

type Route = 'desktop' | 'big-picture';

function Button({label, onPress, primary = false}: {
  label: string;
  onPress: () => void;
  primary?: boolean;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({pressed}) => [styles.button, primary && styles.primaryButton, pressed && styles.pressed]}>
      <Text style={[styles.buttonText, primary && styles.primaryButtonText]}>{label}</Text>
    </Pressable>
  );
}

function DesktopLauncher({
  games,
  settings,
  busy,
  error,
  onChooseFolders,
  onRefresh,
  onLaunch,
  onSaveRoots,
  onBigPicture,
}: {
  games: GameInstall[];
  settings: LauncherSettings;
  busy: boolean;
  error?: string;
  onChooseFolders: () => void;
  onRefresh: () => void;
  onLaunch: (game: GameInstall) => void;
  onSaveRoots: (roots: string[]) => void;
  onBigPicture: () => void;
}) {
  const [selectedPath, setSelectedPath] = useState<string>();
  const [rootsText, setRootsText] = useState(settings.gameDirectories.join('\n'));
  const selected = games.find(game => game.gamePath === selectedPath) ?? games[0];

  useEffect(() => setRootsText(settings.gameDirectories.join('\n')), [settings.gameDirectories]);

  return (
    <SafeAreaView style={styles.desktopRoot}>
      <View style={styles.desktopHeader}>
        <View style={styles.brandRow}>
          <Image source={brandArtwork.desktopDark} style={styles.desktopBrandIcon} />
          <View>
          <Text style={styles.wordmark}>Prosperismo</Text>
          <Text style={styles.subtitle}>Desktop launcher</Text>
          </View>
        </View>
        <View style={styles.actionRow}>
          <Button label="Refresh" onPress={onRefresh} />
          <Button label="Big Picture" onPress={onBigPicture} primary />
        </View>
      </View>

      {!hasNativeProsperismoHost && (
        <Text style={styles.warning}>
          Windows host adapter pending — the React Native shell is active, but native file and process operations are unavailable.
        </Text>
      )}
      {error && <Text style={styles.error}>{error}</Text>}

      <View style={styles.desktopBody}>
        <View style={styles.libraryPanel}>
          <View style={styles.panelTitleRow}>
            <Text style={styles.panelTitle}>Game library</Text>
            <Text style={styles.muted}>{games.length} installed</Text>
          </View>
          <View style={styles.tableHeader}>
            <Text style={[styles.cell, styles.nameCell]}>Name</Text>
            <Text style={styles.cell}>Serial</Text>
            <Text style={styles.cell}>Version</Text>
            <Text style={styles.cell}>Firmware</Text>
          </View>
          <ScrollView style={styles.gameTable}>
            {games.map(game => (
              <Pressable
                key={game.gamePath}
                onPress={() => setSelectedPath(game.gamePath)}
                onLongPress={() => onLaunch(game)}
                style={[styles.tableRow, selected?.gamePath === game.gamePath && styles.selectedRow]}>
                <Text numberOfLines={1} style={[styles.cell, styles.nameCell, styles.rowText]}>{game.titleName}</Text>
                <Text style={[styles.cell, styles.rowText]}>{game.titleId || '—'}</Text>
                <Text style={[styles.cell, styles.rowText]}>{game.gameVersion || '—'}</Text>
                <Text style={[styles.cell, styles.rowText]}>{game.firmwareVersion || '—'}</Text>
              </Pressable>
            ))}
            {!busy && games.length === 0 && (
              <Text style={styles.empty}>Add one or more game folders to scan recursively for eboot.bin.</Text>
            )}
          </ScrollView>
          {selected && (
            <View style={styles.selectionBar}>
              <View style={styles.selectionCopy}>
                <Text style={styles.selectionTitle}>{selected.titleName}</Text>
                <Text numberOfLines={1} style={styles.pathText}>{selected.baseDirectory}</Text>
              </View>
              <Button label="Run" onPress={() => onLaunch(selected)} primary />
            </View>
          )}
        </View>

        <View style={styles.settingsPanel}>
          <Image source={brandArtwork.desktopLight} style={styles.desktopPanelWatermark} />
          <Text style={styles.panelTitle}>Game folders</Text>
          <Text style={styles.fieldHelp}>One root per line. Child directories are scanned breadth-first.</Text>
          <TextInput
            accessibilityLabel="Game folders"
            multiline
            value={rootsText}
            onChangeText={setRootsText}
            placeholder={'D:\\Games\\PS5'}
            placeholderTextColor="#647087"
            style={styles.rootsInput}
          />
          <View style={styles.stack}>
            <Button label="Browse folders…" onPress={onChooseFolders} />
            <Button
              label="Save folders"
              onPress={() => onSaveRoots(rootsText.split(/\r?\n/).map(item => item.trim()).filter(Boolean))}
              primary
            />
          </View>
          <View style={styles.divider} />
          <Text style={styles.panelTitle}>Global emulation</Text>
          <Text style={styles.settingLine}>{settings.global.screenResolution} · {settings.global.vblankFrequency} Hz</Text>
          <Text style={styles.settingLine}>Shader optimization: {settings.global.shaderOptimization}</Text>
          <Text style={styles.settingLine}>Vulkan validation: {settings.global.vulkanValidation ? 'On' : 'Off'}</Text>
        </View>
      </View>
      {busy && <View style={styles.busy}><ActivityIndicator color="#6da8ff" /><Text style={styles.muted}>Scanning…</Text></View>}
    </SafeAreaView>
  );
}

function BigPicture({games, onDesktop, onLaunch}: {
  games: GameInstall[];
  onDesktop: () => void;
  onLaunch: (game: GameInstall) => void;
}) {
  const [surface, setSurface] = useState<'home' | 'settings'>('home');
  const [selected, setSelected] = useState(0);
  const game = games[selected];

  if (surface === 'settings') {
    return (
      <SafeAreaView style={styles.shellRoot}>
        <View style={styles.glowTop} />
        <Image source={brandArtwork.bigPictureDefault} style={styles.shellWatermark} />
        <View style={styles.shellTopBar}>
          <Text style={styles.shellBrand}>Settings</Text>
          <Button label="Home" onPress={() => setSurface('home')} />
        </View>
        <View style={styles.shellSettings}>
          {['System', 'Graphics', 'Audio', 'Controllers', 'Storage', 'About Prosperismo'].map((label, index) => (
            <Pressable key={label} style={[styles.settingsCategory, index === 0 && styles.settingsCategoryFocused]}>
              <View style={styles.settingsGlyph} />
              <Text style={styles.settingsCategoryText}>{label}</Text>
              <Text style={styles.chevron}>›</Text>
            </Pressable>
          ))}
        </View>
        <Text style={styles.shellHint}>F10 returns directly between Home and emulator Settings.</Text>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.shellRoot}>
      <View style={styles.glowTop} />
      <View style={styles.glowSide} />
      <Image source={brandArtwork.bigPicture} style={styles.shellWatermark} />
      <View style={styles.shellTopBar}>
        <View style={styles.navBand}>
          <Text style={[styles.navItem, styles.navItemActive]}>Games</Text>
          <Text style={styles.navItem}>Media</Text>
        </View>
        <View style={styles.systemIcons}>
          <Pressable style={styles.systemButton} onPress={() => setSurface('settings')}><Text style={styles.systemGlyph}>⚙</Text></Pressable>
          <Pressable style={styles.systemButton} onPress={onDesktop}><Text style={styles.systemGlyph}>▦</Text></Pressable>
        </View>
      </View>

      <View style={styles.hero}>
        <Text style={styles.heroEyebrow}>PROSPERISMO</Text>
        <Text style={styles.heroTitle}>{game?.titleName ?? 'Your games live here'}</Text>
        <Text style={styles.heroMeta}>{game ? `${game.titleId || 'Local title'}  ·  ${game.gameVersion || 'Unknown version'}` : 'Add a game folder from Desktop mode.'}</Text>
        {game && <Button label="Play" onPress={() => onLaunch(game)} primary />}
      </View>

      <View style={styles.tileRail}>
        {games.map((item, index) => (
          <Pressable
            key={item.gamePath}
            onPress={() => setSelected(index)}
            onLongPress={() => onLaunch(item)}
            style={[styles.gameTile, index === selected && styles.gameTileFocused]}>
            <View style={styles.tileArtwork}>
              <Text style={styles.tileMonogram}>{item.titleName.slice(0, 1).toUpperCase()}</Text>
            </View>
            <Text numberOfLines={1} style={styles.tileTitle}>{item.titleName}</Text>
          </Pressable>
        ))}
        <Pressable onPress={onDesktop} style={styles.allGamesTile}>
          <Text style={styles.allGamesGlyph}>▦</Text>
          <Text style={styles.tileTitle}>All games</Text>
        </Pressable>
      </View>
      <Text style={styles.shellHint}>Enter select  ·  Esc back  ·  Desktop manages folders and advanced settings</Text>
    </SafeAreaView>
  );
}

export default function App() {
  const [route, setRoute] = useState<Route>('desktop');
  const [settings, setSettings] = useState<LauncherSettings>(DEFAULT_LAUNCHER_SETTINGS);
  const [games, setGames] = useState<GameInstall[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  const refresh = useCallback(async (current: LauncherSettings) => {
    setBusy(true);
    setError(undefined);
    try {
      setGames(await scanGameDirectories(
        prosperismoHost,
        current.gameDirectories,
        current.global,
        current.perGame,
      ));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    loadSettings(prosperismoHost)
      .then(value => {
        setSettings(value);
        return refresh(value);
      })
      .catch(reason => setError(reason instanceof Error ? reason.message : String(reason)));
  }, [refresh]);

  const updateRoots = useCallback(async (roots: string[]) => {
    const next = {...settings, gameDirectories: roots};
    setSettings(next);
    try {
      await saveSettings(prosperismoHost, next);
      await refresh(next);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
    }
  }, [refresh, settings]);

  const chooseFolders = useCallback(async () => {
    try {
      const roots = await prosperismoHost.chooseGameDirectories();
      if (roots.length > 0) {
        await updateRoots([...settings.gameDirectories, ...roots]);
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
    }
  }, [settings.gameDirectories, updateRoots]);

  const run = useCallback(async (game: GameInstall) => {
    setError(undefined);
    try {
      await launchGame(prosperismoHost, game, settings);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
      setRoute('desktop');
    }
  }, [settings]);

  const desktop = useMemo(() => (
    <DesktopLauncher
      games={games}
      settings={settings}
      busy={busy}
      error={error}
      onChooseFolders={chooseFolders}
      onRefresh={() => refresh(settings)}
      onLaunch={run}
      onSaveRoots={updateRoots}
      onBigPicture={() => setRoute('big-picture')}
    />
  ), [busy, chooseFolders, error, games, refresh, run, settings, updateRoots]);

  return route === 'desktop'
    ? desktop
    : <BigPicture games={games} onDesktop={() => setRoute('desktop')} onLaunch={run} />;
}

const styles = StyleSheet.create({
  desktopRoot: {flex: 1, backgroundColor: '#0b0f16', padding: 22},
  desktopHeader: {flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 18},
  brandRow: {flexDirection: 'row', alignItems: 'center', gap: 12},
  desktopBrandIcon: {width: 48, height: 48, borderRadius: 10},
  desktopPanelWatermark: {position: 'absolute', right: -30, bottom: -30, width: 180, height: 180, opacity: 0.055},
  wordmark: {fontSize: 30, fontWeight: '700', color: '#f5f8ff', letterSpacing: 0.4},
  subtitle: {fontSize: 13, color: '#8491a8', marginTop: 3},
  actionRow: {flexDirection: 'row', gap: 10},
  button: {minHeight: 38, justifyContent: 'center', paddingHorizontal: 17, borderRadius: 7, borderWidth: 1, borderColor: '#354054', backgroundColor: '#151c28'},
  primaryButton: {backgroundColor: '#f4f7ff', borderColor: '#ffffff'},
  buttonText: {color: '#dce5f5', fontWeight: '600'},
  primaryButtonText: {color: '#151a23'},
  pressed: {opacity: 0.72, transform: [{scale: 0.985}]},
  warning: {color: '#e8c46c', backgroundColor: '#2a2212', borderWidth: 1, borderColor: '#5e4d23', padding: 10, marginBottom: 10, borderRadius: 6},
  error: {color: '#ffb8b8', backgroundColor: '#351819', padding: 10, marginBottom: 10, borderRadius: 6},
  desktopBody: {flex: 1, flexDirection: 'row', gap: 14},
  libraryPanel: {flex: 1, borderWidth: 1, borderColor: '#283144', backgroundColor: '#101722', borderRadius: 9, overflow: 'hidden'},
  settingsPanel: {width: 300, borderWidth: 1, borderColor: '#283144', backgroundColor: '#101722', borderRadius: 9, padding: 16},
  panelTitleRow: {padding: 15, flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center'},
  panelTitle: {fontSize: 16, fontWeight: '700', color: '#eef4ff'},
  muted: {color: '#8290a8'},
  tableHeader: {flexDirection: 'row', backgroundColor: '#181f2b', borderTopWidth: 1, borderBottomWidth: 1, borderColor: '#293246', paddingVertical: 9, paddingHorizontal: 11},
  tableRow: {flexDirection: 'row', paddingVertical: 11, paddingHorizontal: 11, borderBottomWidth: 1, borderColor: '#202a3a'},
  selectedRow: {backgroundColor: '#183e69'},
  cell: {width: 115, color: '#9ca9bd', fontSize: 12},
  nameCell: {flex: 1, width: undefined},
  rowText: {fontSize: 13, color: '#dbe4f3'},
  gameTable: {flex: 1},
  empty: {color: '#7e8ba1', padding: 24, textAlign: 'center'},
  selectionBar: {flexDirection: 'row', alignItems: 'center', padding: 13, borderTopWidth: 1, borderColor: '#293246', backgroundColor: '#151d29'},
  selectionCopy: {flex: 1, paddingRight: 12},
  selectionTitle: {color: '#f2f6ff', fontWeight: '700'},
  pathText: {color: '#7f8ca2', fontSize: 11, marginTop: 3},
  fieldHelp: {color: '#7f8ca2', fontSize: 12, marginTop: 8, lineHeight: 17},
  rootsInput: {height: 130, marginTop: 12, marginBottom: 12, padding: 10, textAlignVertical: 'top', color: '#e4ebf8', backgroundColor: '#0a1019', borderWidth: 1, borderColor: '#303b50', borderRadius: 6},
  stack: {gap: 8},
  divider: {height: 1, backgroundColor: '#293246', marginVertical: 18},
  settingLine: {color: '#a9b5c7', marginTop: 10},
  busy: {position: 'absolute', bottom: 20, right: 340, flexDirection: 'row', gap: 8, alignItems: 'center'},
  shellRoot: {flex: 1, backgroundColor: '#071424', paddingHorizontal: 62, paddingVertical: 34, overflow: 'hidden'},
  glowTop: {position: 'absolute', top: -260, left: 180, width: 900, height: 600, borderRadius: 450, backgroundColor: '#183b61', opacity: 0.62},
  glowSide: {position: 'absolute', right: -250, bottom: -260, width: 720, height: 720, borderRadius: 360, backgroundColor: '#102c4c', opacity: 0.65},
  shellWatermark: {position: 'absolute', right: 65, top: 125, width: 430, height: 430, opacity: 0.16},
  shellTopBar: {zIndex: 1, flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center'},
  shellBrand: {color: '#f5f8ff', fontSize: 27, fontWeight: '500'},
  navBand: {flexDirection: 'row', gap: 34},
  navItem: {color: '#93a2b5', fontSize: 22},
  navItemActive: {color: '#ffffff', fontWeight: '700'},
  systemIcons: {flexDirection: 'row', gap: 16},
  systemButton: {width: 56, height: 56, borderRadius: 28, alignItems: 'center', justifyContent: 'center', backgroundColor: '#ffffff18'},
  systemGlyph: {fontSize: 25, color: '#ffffff'},
  hero: {zIndex: 1, marginTop: 90, width: 650, alignItems: 'flex-start'},
  heroEyebrow: {fontSize: 13, letterSpacing: 4, color: '#88b9e2'},
  heroTitle: {fontSize: 48, lineHeight: 56, color: '#ffffff', fontWeight: '300', marginTop: 10, marginBottom: 10},
  heroMeta: {fontSize: 15, color: '#a9bacd', marginBottom: 24},
  tileRail: {zIndex: 1, flexDirection: 'row', alignItems: 'flex-start', gap: 18, marginTop: 65},
  gameTile: {width: 146, padding: 6, borderRadius: 14},
  gameTileFocused: {backgroundColor: '#ffffff', transform: [{scale: 1.08}]},
  tileArtwork: {height: 132, borderRadius: 9, alignItems: 'center', justifyContent: 'center', backgroundColor: '#244f77'},
  tileMonogram: {color: '#dceeff', fontSize: 50, fontWeight: '200'},
  tileTitle: {color: '#edf5ff', marginTop: 10, fontSize: 13},
  allGamesTile: {width: 146, padding: 6, alignItems: 'center'},
  allGamesGlyph: {height: 132, width: 132, borderRadius: 66, backgroundColor: '#ffffff14', color: '#ffffff', fontSize: 46, textAlign: 'center', textAlignVertical: 'center'},
  shellHint: {position: 'absolute', right: 50, bottom: 28, color: '#7890aa', fontSize: 12},
  shellSettings: {zIndex: 1, marginTop: 70, width: 670},
  settingsCategory: {height: 62, flexDirection: 'row', alignItems: 'center', borderRadius: 7, paddingHorizontal: 13, marginBottom: 5},
  settingsCategoryFocused: {backgroundColor: '#f8fbff'},
  settingsGlyph: {width: 34, height: 34, borderRadius: 17, backgroundColor: '#3f678e', marginRight: 18},
  settingsCategoryText: {flex: 1, color: '#b8c8d9', fontSize: 20},
  chevron: {color: '#647e98', fontSize: 30},
});
