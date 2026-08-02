#pragma once

#include "JSValue.h"
#include "NativeModules.h"

#include <optional>
#include <string>
#include <vector>

namespace winrt::Prosperismo {

REACT_STRUCT(ProsperismoDirectoryEntry)
struct ProsperismoDirectoryEntry {
  REACT_FIELD(name)
  std::string name;

  REACT_FIELD(path)
  std::string path;

  REACT_FIELD(kind)
  std::string kind;

  REACT_FIELD(symbolicLink)
  bool symbolicLink{false};
};

REACT_MODULE(ProsperismoHost, L"ProsperismoHost")
struct ProsperismoHost {
  REACT_INIT(Initialize)
  void Initialize(winrt::Microsoft::ReactNative::ReactContext const &context) noexcept {
    m_context = context;
  }

  REACT_METHOD(ListDirectory, L"listDirectory")
  void ListDirectory(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<std::vector<ProsperismoDirectoryEntry>> &&promise) noexcept;

  REACT_METHOD(ReadTextFile, L"readTextFile")
  void ReadTextFile(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<std::string> &&promise) noexcept;

  REACT_METHOD(ReadBinaryFile, L"readBinaryFile")
  void ReadBinaryFile(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<std::vector<uint8_t>> &&promise) noexcept;

  REACT_METHOD(WriteTextFile, L"writeTextFile")
  void WriteTextFile(
      std::string path,
      std::string contents,
      winrt::Microsoft::ReactNative::ReactPromise<void> &&promise) noexcept;

  REACT_METHOD(CanonicalizePath, L"canonicalizePath")
  void CanonicalizePath(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<std::string> &&promise) noexcept;

  REACT_METHOD(ChooseGameDirectories, L"chooseGameDirectories")
  void ChooseGameDirectories(
      winrt::Microsoft::ReactNative::ReactPromise<std::vector<std::string>> &&promise) noexcept;

  REACT_METHOD(LoadLauncherSettings, L"loadLauncherSettings")
  void LoadLauncherSettings(
      winrt::Microsoft::ReactNative::ReactPromise<std::optional<std::string>> &&promise) noexcept;

  REACT_METHOD(SaveLauncherSettings, L"saveLauncherSettings")
  void SaveLauncherSettings(
      std::string json,
      winrt::Microsoft::ReactNative::ReactPromise<void> &&promise) noexcept;

  REACT_METHOD(FindEmulator, L"findEmulator")
  void FindEmulator(
      winrt::Microsoft::ReactNative::ReactPromise<std::string> &&promise) noexcept;

  REACT_METHOD(FileExists, L"fileExists")
  void FileExists(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<bool> &&promise) noexcept;

  REACT_METHOD(OpenPath, L"openPath")
  void OpenPath(
      std::string path,
      winrt::Microsoft::ReactNative::ReactPromise<void> &&promise) noexcept;

  REACT_METHOD(RemoveDirectories, L"removeDirectories")
  void RemoveDirectories(
      std::vector<std::string> paths,
      std::string titleId,
      bool confirmed,
      winrt::Microsoft::ReactNative::ReactPromise<std::vector<std::string>> &&promise) noexcept;

  REACT_METHOD(Launch, L"launch")
  void Launch(
      std::string executable,
      std::vector<std::string> arguments,
      std::string workingDirectory,
      winrt::Microsoft::ReactNative::ReactPromise<void> &&promise) noexcept;

 private:
  winrt::Microsoft::ReactNative::ReactContext m_context{nullptr};
};

} // namespace winrt::Prosperismo
