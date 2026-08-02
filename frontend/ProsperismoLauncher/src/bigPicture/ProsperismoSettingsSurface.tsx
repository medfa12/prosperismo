import React, {useEffect, useRef, useState} from 'react';
import {Animated, Easing, findNodeHandle, Pressable, StyleSheet, Text, UIManager, View} from 'react-native';
import type {LauncherSettings} from '../core/models';
import {SHELL_METRICS} from './shellMetrics';

export const PROSPERISMO_SETTINGS_CATEGORIES = [
  ['General', 'Game folders, library order, and launcher behavior'],
  ['Graphics', 'Resolution, presentation, and Vulkan diagnostics'],
  ['Audio and Interface', 'Controller and keyboard input'],
  ['Emulation', 'Shaders and compatibility defaults'],
  ['Logging', 'Shader, command-buffer, and printf output'],
  ['Environment', 'Title patches and compatibility profiles'],
  ['About Prosperismo', 'Version, diagnostics, and legal notices'],
] as const;

const LIBRARY_SORT_FIELDS = ['titleName', 'titleId', 'gameVersion', 'firmwareVersion', 'gamePath', 'status', 'comment'] as const;
const SHADER_OPTIMIZATIONS = ['None', 'Size', 'Performance'] as const;

type FocusableUIManager = typeof UIManager & {focus(reactTag: number): void};
type SettingRow = {label: string; value: string; onPress?: () => void};

function focusNative(target: unknown): void {
  const tag = findNodeHandle(target as any);
  if (tag !== null) {
    (UIManager as FocusableUIManager).focus(tag);
  }
}

function nextValue<T>(values: readonly T[], value: T): T {
  return values[(values.indexOf(value) + 1) % values.length];
}

function SettingsFocus({active}: {active: boolean}) {
  const phase = useRef(new Animated.Value(active ? 1 : 0)).current;
  useEffect(() => {
    const animation = Animated.timing(phase, {toValue: active ? 1 : 0, duration: active ? 250 : 180, easing: Easing.out(Easing.exp), useNativeDriver: true});
    animation.start();
    return () => animation.stop();
  }, [active, phase]);
  return <Animated.View pointerEvents="none" style={[styles.focus, {opacity: phase}]} />;
}

function CategoryGlyph({index}: {index: number}) {
  return <View style={styles.categoryGlyph}>
    <View style={[styles.glyphMark, index % 3 === 0 && styles.glyphRound, index % 3 === 1 && styles.glyphDiamond]} />
    {index % 3 === 2 && <View style={styles.glyphInner} />}
  </View>;
}

export function ProsperismoSettingsRoot({selectedIndex, onSelect, onActivate, onRef}: {
  selectedIndex: number;
  onSelect(index: number): void;
  onActivate(index: number): void;
  onRef(index: number, node: any): void;
}) {
  return <View style={styles.stage}>
    <Text style={styles.pageTitle}>Settings</Text>
    <View style={styles.categoryList}>
      {PROSPERISMO_SETTINGS_CATEGORIES.map(([name, description], index) => <Pressable
        ref={node => onRef(index, node)}
        accessibilityLabel={`${name}. ${description}`}
        accessibilityRole="button"
        key={name}
        onFocus={() => onSelect(index)}
        onPress={() => onActivate(index)}
        style={styles.categoryRow}>
        <SettingsFocus active={index === selectedIndex} />
        <CategoryGlyph index={index} />
        <View style={styles.categoryCopy}>
          <Text style={styles.categoryTitle}>{name}</Text>
          <Text numberOfLines={1} style={styles.categoryDescription}>{description}</Text>
        </View>
        <Text style={styles.chevron}>›</Text>
      </Pressable>)}
    </View>
  </View>;
}

export function ProsperismoSettingsDetail({categoryIndex, settings, onSave, onBack}: {
  categoryIndex: number;
  settings: LauncherSettings;
  onSave(next: LauncherSettings): void;
  onBack(): void;
}) {
  const [focusedIndex, setFocusedIndex] = useState(0);
  const refs = useRef<any[]>([]);
  const category = PROSPERISMO_SETTINGS_CATEGORIES[categoryIndex] ?? PROSPERISMO_SETTINGS_CATEGORIES[0];
  const updateGlobal = <K extends keyof LauncherSettings['global']>(key: K, value: LauncherSettings['global'][K]) => onSave({...settings, global: {...settings.global, [key]: value}});
  const rows: SettingRow[] = categoryIndex === 0 ? [
    {label: 'Game folders', value: `${settings.gameDirectories.length} configured`},
    {label: 'Library sort', value: settings.library.sortField, onPress: () => onSave({...settings, library: {...settings.library, sortField: nextValue(LIBRARY_SORT_FIELDS, settings.library.sortField)}})},
    {label: 'Sort direction', value: settings.library.sortDirection, onPress: () => onSave({...settings, library: {...settings.library, sortDirection: settings.library.sortDirection === 'ascending' ? 'descending' : 'ascending'}})},
  ] : categoryIndex === 1 ? [
    {label: 'Resolution', value: settings.global.screenResolution, onPress: () => updateGlobal('screenResolution', settings.global.screenResolution === '1280x720' ? '1920x1080' : '1280x720')},
    {label: 'Vblank frequency', value: `${settings.global.vblankFrequency} Hz`, onPress: () => updateGlobal('vblankFrequency', settings.global.vblankFrequency === 60 ? 120 : 60)},
    {label: 'Vulkan validation', value: settings.global.vulkanValidation ? 'On' : 'Off', onPress: () => updateGlobal('vulkanValidation', !settings.global.vulkanValidation)},
    {label: 'RenderDoc', value: settings.global.renderDoc ? 'On' : 'Off', onPress: () => updateGlobal('renderDoc', !settings.global.renderDoc)},
  ] : categoryIndex === 2 ? [
    {label: 'Controller mapping', value: 'Windows host'},
    {label: 'Keyboard input', value: 'Windows host'},
  ] : categoryIndex === 3 ? [
    {label: 'Shader optimization', value: settings.global.shaderOptimization, onPress: () => updateGlobal('shaderOptimization', nextValue(SHADER_OPTIMIZATIONS, settings.global.shaderOptimization))},
    {label: 'Shader validation', value: settings.global.shaderValidation ? 'On' : 'Off', onPress: () => updateGlobal('shaderValidation', !settings.global.shaderValidation)},
    {label: 'NGG rectlist draw', value: settings.global.nggRectlistDraw ? 'On' : 'Off', onPress: () => updateGlobal('nggRectlistDraw', !settings.global.nggRectlistDraw)},
  ] : categoryIndex === 4 ? [
    {label: 'Shader log direction', value: settings.global.shaderLogDirection, onPress: () => updateGlobal('shaderLogDirection', nextValue(['Silent', 'Console', 'File'] as const, settings.global.shaderLogDirection))},
    {label: 'Shader log folder', value: settings.global.shaderLogFolder},
    {label: 'Buffer dump', value: settings.global.commandBufferDump ? 'On' : 'Off', onPress: () => updateGlobal('commandBufferDump', !settings.global.commandBufferDump)},
    {label: 'Printf output', value: settings.global.printfDirection, onPress: () => updateGlobal('printfDirection', nextValue(['Silent', 'Console', 'File'] as const, settings.global.printfDirection))},
  ] : categoryIndex === 5 ? [
    {label: 'Patch titles', value: `${Object.keys(settings.patchSelections).length} configured`},
    {label: 'Compatibility profiles', value: `${Object.keys(settings.compatibility).length} imported`},
  ] : [
    {label: 'Prosperismo', value: 'React Native Windows shell'},
    {label: 'Presentation', value: 'Firmware-derived contracts'},
  ];
  useEffect(() => {
    focusNative(refs.current[0]);
  }, [categoryIndex]);
  const onKeyDown = (event: any) => {
    const key = event?.nativeEvent?.key;
    if (key === 'Escape' || key === 'GamepadB') {
      onBack();
      event.stopPropagation?.();
      return;
    }
    if (key === 'ArrowDown' || key === 'GamepadDPadDown' || key === 'ArrowUp' || key === 'GamepadDPadUp') {
      const delta = key === 'ArrowDown' || key === 'GamepadDPadDown' ? 1 : -1;
      const next = Math.max(0, Math.min(rows.length - 1, focusedIndex + delta));
      setFocusedIndex(next);
      focusNative(refs.current[next]);
      event.stopPropagation?.();
    }
  };
  return <View style={styles.stage} {...({onKeyDownCapture: onKeyDown} as any)}>
    <Pressable accessibilityRole="button" onPress={onBack} style={styles.backTarget}><Text style={styles.backText}>‹ Settings</Text></Pressable>
    <Text style={styles.detailTitle}>{category[0]}</Text>
    <Text style={styles.detailDescription}>{category[1]}</Text>
    <View style={styles.detailList}>
      {rows.map((row, index) => <Pressable
        ref={node => { refs.current[index] = node; }}
        accessibilityRole="button"
        key={row.label}
        onFocus={() => setFocusedIndex(index)}
        onPress={row.onPress}
        style={styles.detailRow}>
        <SettingsFocus active={focusedIndex === index} />
        <Text style={styles.detailLabel}>{row.label}</Text>
        <Text numberOfLines={1} style={styles.detailValue}>{row.value}</Text>
      </Pressable>)}
    </View>
  </View>;
}

const styles = StyleSheet.create({
  stage: {position: 'absolute', inset: 0},
  pageTitle: {position: 'absolute', left: 304, top: 80, color: '#fff', fontFamily: 'Segoe UI', fontSize: 42, fontWeight: '400'},
  categoryList: {position: 'absolute', left: 304, top: 186, width: 1312, height: 894},
  categoryRow: {height: 112, paddingHorizontal: 16, flexDirection: 'row', alignItems: 'center'},
  focus: {position: 'absolute', left: 0, top: 3, right: 0, bottom: 5, borderWidth: SHELL_METRICS.focusLineWidth, borderColor: 'rgba(255,255,255,0.94)', borderRadius: 16},
  categoryGlyph: {width: 48, height: 48, marginRight: 20, alignItems: 'center', justifyContent: 'center'},
  glyphMark: {width: 28, height: 28, borderWidth: 3, borderColor: '#fff'}, glyphRound: {borderRadius: 14}, glyphDiamond: {transform: [{rotate: '45deg'}]}, glyphInner: {position: 'absolute', width: 10, height: 10, borderRadius: 5, backgroundColor: '#fff'},
  categoryCopy: {flex: 1}, categoryTitle: {color: '#fff', fontFamily: 'Segoe UI', fontSize: 28}, categoryDescription: {marginTop: 5, color: 'rgba(255,255,255,0.7)', fontFamily: 'Segoe UI', fontSize: 18}, chevron: {width: 52, color: '#fff', fontFamily: 'Segoe UI', fontSize: 42, textAlign: 'center'},
  backTarget: {position: 'absolute', left: 304, top: 62, paddingVertical: 10, paddingRight: 28}, backText: {color: 'rgba(255,255,255,0.72)', fontFamily: 'Segoe UI', fontSize: 20},
  detailTitle: {position: 'absolute', left: 304, top: 112, color: '#fff', fontFamily: 'Segoe UI', fontSize: 42}, detailDescription: {position: 'absolute', left: 304, top: 166, color: 'rgba(255,255,255,0.7)', fontFamily: 'Segoe UI', fontSize: 20},
  detailList: {position: 'absolute', left: 304, top: 224, width: 1312}, detailRow: {height: 96, borderRadius: 16, paddingHorizontal: 24, flexDirection: 'row', alignItems: 'center'}, detailLabel: {flex: 1, color: '#fff', fontFamily: 'Segoe UI', fontSize: 26}, detailValue: {maxWidth: 540, color: 'rgba(255,255,255,0.72)', fontFamily: 'Segoe UI', fontSize: 22, textAlign: 'right'},
});
