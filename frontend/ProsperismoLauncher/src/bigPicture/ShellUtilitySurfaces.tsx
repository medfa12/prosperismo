import React, {useEffect, useMemo, useRef, useState} from 'react';
import {
  Animated,
  Easing,
  findNodeHandle,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  UIManager,
  View,
} from 'react-native';
import type {GameInstall} from '../core/models';
import {SHELL_METRICS} from './shellMetrics';
import {shellTextStyle} from './shellTypography';

type FocusableUIManager = typeof UIManager & {focus(reactTag: number): void};

function focusNative(target: unknown): void {
  const tag = findNodeHandle(target as any);
  if (tag !== null) {
    (UIManager as FocusableUIManager).focus(tag);
  }
}

function FocusLine({active, radius = 16}: {active: boolean; radius?: number}) {
  const phase = useRef(new Animated.Value(active ? 1 : 0)).current;
  useEffect(() => {
    const animation = Animated.timing(phase, {
      toValue: active ? 1 : 0,
      duration: active ? 250 : 180,
      easing: Easing.out(Easing.exp),
      useNativeDriver: true,
    });
    animation.start();
    return () => animation.stop();
  }, [active, phase]);
  return <Animated.View pointerEvents="none" style={[styles.focusLine, {borderRadius: radius, opacity: phase}]} />;
}

/** Neutral local-user avatar. It intentionally has no Sony account imagery. */
export function GenericAvatar({color = '#f5f7fa'}: {color?: any}) {
  return <View accessibilityElementsHidden importantForAccessibility="no-hide-descendants" style={styles.avatarGlyph}>
    <View style={[styles.avatarHead, {backgroundColor: color}]} />
    <View style={[styles.avatarShoulders, {backgroundColor: color}]} />
  </View>;
}

type PromptKind = 'confirm' | 'back' | 'options';

function PromptMark({kind}: {kind: PromptKind}) {
  if (kind === 'back') {
    return <View style={styles.promptCircle} />;
  }
  if (kind === 'options') {
    return <View style={styles.promptOptions}>{[0, 1, 2].map(index => <View key={index} style={styles.promptOptionsLine} />)}</View>;
  }
  return <View style={styles.promptCross}><View style={styles.promptCrossA} /><View style={styles.promptCrossB} /></View>;
}

export interface ButtonPrompt {
  kind: PromptKind;
  label: string;
}

export function ShellButtonPrompts({prompts}: {prompts: readonly ButtonPrompt[]}) {
  return <View pointerEvents="none" style={styles.promptBar}>
    {prompts.map(prompt => <View key={`${prompt.kind}-${prompt.label}`} style={styles.promptItem}>
      <PromptMark kind={prompt.kind} />
      <Text style={styles.promptLabel}>{prompt.label}</Text>
    </View>)}
  </View>;
}

export function SearchSurface({games, onClose, onLaunch}: {
  games: readonly GameInstall[];
  onClose(): void;
  onLaunch(game: GameInstall): void;
}) {
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<TextInput>(null);
  const resultRefs = useRef<any[]>([]);
  const results = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase();
    if (!needle) {
      return games.slice(0, 8);
    }
    return games.filter(game => `${game.titleName} ${game.titleId}`.toLocaleLowerCase().includes(needle)).slice(0, 8);
  }, [games, query]);
  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);
  useEffect(() => {
    inputRef.current?.focus();
  }, []);
  const activate = () => {
    const game = results[selectedIndex];
    if (game) {
      onLaunch(game);
    }
  };
  const onKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (key === 'Escape' || key === 'GamepadB') {
      onClose();
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowDown' || key === 'GamepadDPadDown') {
      const next = Math.min(results.length - 1, selectedIndex + 1);
      setSelectedIndex(Math.max(0, next));
      focusNative(resultRefs.current[next]);
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowUp' || key === 'GamepadDPadUp') {
      if (selectedIndex === 0) {
        inputRef.current?.focus();
      } else {
        const next = selectedIndex - 1;
        setSelectedIndex(next);
        focusNative(resultRefs.current[next]);
      }
      event.stopPropagation?.();
    }
  };
  return <View style={styles.fullSurface} {...({onKeyDownCapture: onKeyDown} as any)}>
    <Text style={styles.pageTitle}>Search</Text>
    <View style={styles.searchBox}>
      <View style={styles.searchGlyph}><View style={styles.searchLens} /><View style={styles.searchHandle} /></View>
      <TextInput
        ref={inputRef}
        accessibilityLabel="Search games"
        autoFocus
        onChangeText={setQuery}
        onSubmitEditing={activate}
        placeholder="Search games"
        placeholderTextColor="rgba(255,255,255,0.48)"
        selectionColor="#ffffff"
        style={styles.searchInput}
        value={query}
      />
    </View>
    <Text style={styles.resultHeading}>{query.trim() ? `${results.length} results` : 'Games'}</Text>
    <View style={styles.resultsList}>
      {results.map((game, index) => <Pressable
        ref={node => { resultRefs.current[index] = node; }}
        accessibilityRole="button"
        key={game.gamePath}
        onFocus={() => setSelectedIndex(index)}
        onPress={() => onLaunch(game)}
        style={styles.resultRow}>
        <FocusLine active={selectedIndex === index} />
        <View style={styles.resultMonogram}><Text style={styles.resultMonogramText}>{game.titleName.slice(0, 1).toUpperCase()}</Text></View>
        <View style={styles.resultCopy}><Text numberOfLines={1} style={styles.resultTitle}>{game.titleName}</Text><Text style={styles.resultMeta}>{game.titleId || 'Local game'}</Text></View>
      </Pressable>)}
      {results.length === 0 && <Text style={styles.emptyText}>No games match “{query.trim()}”.</Text>}
    </View>
    <ShellButtonPrompts prompts={[{kind: 'confirm', label: 'Select'}, {kind: 'back', label: 'Back'}]} />
  </View>;
}

export function ProfileMenu({onClose, onDesktop}: {onClose(): void; onDesktop(): void}) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const refs = useRef<any[]>([]);
  const items = [
    {label: 'Desktop Mode', action: onDesktop},
    {label: 'Close', action: onClose},
  ];
  useEffect(() => {
    focusNative(refs.current[0]);
  }, []);
  const onKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (key === 'Escape' || key === 'GamepadB') {
      onClose();
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowDown' || key === 'GamepadDPadDown' || key === 'ArrowUp' || key === 'GamepadDPadUp') {
      const next = key === 'ArrowDown' || key === 'GamepadDPadDown' ? Math.min(1, selectedIndex + 1) : Math.max(0, selectedIndex - 1);
      setSelectedIndex(next);
      focusNative(refs.current[next]);
      event.stopPropagation?.();
    }
  };
  return <View style={styles.menuLayer} {...({onKeyDownCapture: onKeyDown} as any)}>
    <Pressable accessibilityLabel="Close profile menu" onPress={onClose} style={styles.menuScrim} />
    <View style={styles.profilePanel}>
      <View style={styles.profileHeader}><View style={styles.profileAvatar}><GenericAvatar /></View><View><Text style={styles.profileName}>Local User</Text><Text style={styles.profileStatus}>Prosperismo</Text></View></View>
      <View style={styles.profileDivider} />
      {items.map((item, index) => <Pressable ref={node => { refs.current[index] = node; }} accessibilityRole="button" key={item.label} onFocus={() => setSelectedIndex(index)} onPress={item.action} style={styles.menuRow}><FocusLine active={selectedIndex === index} /><Text style={styles.menuRowText}>{item.label}</Text></Pressable>)}
    </View>
    <ShellButtonPrompts prompts={[{kind: 'confirm', label: 'Select'}, {kind: 'back', label: 'Back'}]} />
  </View>;
}

const styles = StyleSheet.create({
  focusLine: {position: 'absolute', left: 0, top: 3, right: 0, bottom: 5, zIndex: 1, borderWidth: SHELL_METRICS.focusLineWidth, borderColor: 'rgba(255,255,255,0.94)'},
  avatarGlyph: {width: 40, height: 40, alignItems: 'center', justifyContent: 'center', overflow: 'hidden'},
  avatarHead: {position: 'absolute', top: 5, width: 14, height: 14, borderRadius: 7},
  avatarShoulders: {position: 'absolute', top: 22, width: 30, height: 22, borderRadius: 15},
  promptBar: {position: 'absolute', right: 84, bottom: 42, height: 36, flexDirection: 'row', alignItems: 'center', gap: 32},
  promptItem: {flexDirection: 'row', alignItems: 'center'},
  promptLabel: {color: '#fff', marginLeft: 10, ...shellTextStyle('Size3XSmall')},
  promptCircle: {width: 20, height: 20, borderWidth: 2, borderRadius: 10, borderColor: '#fff'},
  promptCross: {width: 20, height: 20},
  promptCrossA: {position: 'absolute', left: 9, top: 0, width: 2, height: 20, backgroundColor: '#fff', transform: [{rotate: '45deg'}]},
  promptCrossB: {position: 'absolute', left: 9, top: 0, width: 2, height: 20, backgroundColor: '#fff', transform: [{rotate: '-45deg'}]},
  promptOptions: {width: 22, height: 18, justifyContent: 'space-between', paddingVertical: 2},
  promptOptionsLine: {height: 2, width: 22, borderRadius: 1, backgroundColor: '#fff'},
  fullSurface: {position: 'absolute', inset: 0, zIndex: 15, backgroundColor: 'rgba(2,4,8,0.96)'},
  pageTitle: {position: 'absolute', left: 304, top: 76, color: '#fff', ...shellTextStyle('SizeXLarge')},
  searchBox: {position: 'absolute', left: 304, top: 166, width: 1130, height: 72, borderRadius: 16, flexDirection: 'row', alignItems: 'center', backgroundColor: 'rgba(255,255,255,0.12)', borderWidth: 2, borderColor: 'rgba(255,255,255,0.5)'},
  searchGlyph: {width: 34, height: 34, marginLeft: 24, marginRight: 20},
  searchLens: {position: 'absolute', left: 3, top: 3, width: 20, height: 20, borderWidth: 3, borderRadius: 10, borderColor: '#fff'},
  searchHandle: {position: 'absolute', left: 22, top: 23, width: 13, height: 3, borderRadius: 2, backgroundColor: '#fff', transform: [{rotate: '47deg'}]},
  searchInput: {flex: 1, height: 68, paddingVertical: 0, paddingRight: 24, color: '#fff', ...shellTextStyle('SizeNormal')},
  resultHeading: {position: 'absolute', left: 304, top: 280, color: 'rgba(255,255,255,0.7)', ...shellTextStyle('Size2XSmall')},
  resultsList: {position: 'absolute', left: 304, top: 318, width: 1130},
  resultRow: {height: 82, borderRadius: 16, paddingHorizontal: 16, flexDirection: 'row', alignItems: 'center'},
  resultMonogram: {width: 56, height: 56, borderRadius: 10, marginRight: 20, backgroundColor: 'rgba(255,255,255,0.12)', alignItems: 'center', justifyContent: 'center'},
  resultMonogramText: {color: '#fff', ...shellTextStyle('SizeSmall', '600')},
  resultCopy: {flex: 1}, resultTitle: {color: '#fff', ...shellTextStyle('SizeXSmall')}, resultMeta: {marginTop: 3, color: 'rgba(255,255,255,0.62)', ...shellTextStyle('Size4XSmall')},
  emptyText: {marginTop: 60, color: 'rgba(255,255,255,0.7)', ...shellTextStyle('SizeXSmall')},
  menuLayer: {position: 'absolute', inset: 0, zIndex: 25}, menuScrim: {position: 'absolute', inset: 0, backgroundColor: 'rgba(0,0,0,0.65)'},
  profilePanel: {position: 'absolute', top: 126, left: 1188, width: 652, minHeight: 306, maxHeight: 810, borderRadius: 16, overflow: 'hidden', padding: 8, backgroundColor: '#080a0f'},
  profileHeader: {height: 104, paddingHorizontal: 24, flexDirection: 'row', alignItems: 'center'}, profileAvatar: {width: 64, height: 64, borderRadius: 32, marginRight: 20, backgroundColor: '#39404a', alignItems: 'center', justifyContent: 'center'},
  profileName: {color: '#fff', ...shellTextStyle('SizeXSmall')}, profileStatus: {marginTop: 3, color: 'rgba(255,255,255,0.62)', ...shellTextStyle('Size4XSmall')}, profileDivider: {height: 2, marginHorizontal: 8, marginBottom: 2, backgroundColor: 'rgba(255,255,255,0.1)'},
  menuRow: {height: 90, paddingHorizontal: 24, justifyContent: 'center'}, menuRowText: {color: '#fff', ...shellTextStyle('SizeXSmall')},
});
