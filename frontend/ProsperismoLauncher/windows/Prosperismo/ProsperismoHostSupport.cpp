#define NOMINMAX 1
#define WIN32_LEAN_AND_MEAN 1
#include <windows.h>

#include "ProsperismoHostSupport.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <system_error>
#include <thread>

#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>

namespace prosperismo::host {
namespace {

std::runtime_error WindowsError(char const *operation, DWORD error = GetLastError()) {
  return std::runtime_error(std::string(operation) + " failed with Windows error " + std::to_string(error));
}

std::filesystem::path ExecutableDirectory() {
  std::wstring buffer(260, L'\0');
  for (;;) {
    DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0) {
      throw WindowsError("GetModuleFileNameW");
    }
    if (length < buffer.size()) {
      buffer.resize(length);
      return std::filesystem::path{buffer}.parent_path();
    }
    if (buffer.size() >= 32768) {
      throw std::runtime_error("The launcher executable path exceeds the Windows path limit.");
    }
    buffer.resize(std::min<size_t>(buffer.size() * 2, 32768));
  }
}

std::filesystem::path AbsoluteNormalized(std::filesystem::path const &path) {
  std::error_code error;
  auto absolute = std::filesystem::absolute(path, error);
  if (error) {
    throw std::filesystem::filesystem_error("Could not make path absolute", path, error);
  }
  return absolute.lexically_normal();
}

void AddCandidate(
    std::vector<std::filesystem::path> &candidates,
    std::filesystem::path const &candidate) {
  auto normalized = candidate.lexically_normal();
  if (std::find(candidates.begin(), candidates.end(), normalized) == candidates.end()) {
    candidates.push_back(std::move(normalized));
  }
}

std::wstring ShellItemPath(IShellItem *item) {
  PWSTR rawPath = nullptr;
  auto result = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
  if (FAILED(result)) {
    throw std::runtime_error("The selected shell item is not a filesystem directory.");
  }
  std::wstring path{rawPath};
  CoTaskMemFree(rawPath);
  return path;
}

} // namespace

std::wstring Utf8ToWide(std::string const &value) {
  if (value.empty()) {
    return {};
  }
  if (value.size() > static_cast<size_t>((std::numeric_limits<int>::max)())) {
    throw std::invalid_argument("UTF-8 input is too large.");
  }
  int required = MultiByteToWideChar(
      CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
  if (required == 0) {
    throw WindowsError("MultiByteToWideChar");
  }
  std::wstring result(static_cast<size_t>(required), L'\0');
  if (MultiByteToWideChar(
          CP_UTF8,
          MB_ERR_INVALID_CHARS,
          value.data(),
          static_cast<int>(value.size()),
          result.data(),
          required) == 0) {
    throw WindowsError("MultiByteToWideChar");
  }
  return result;
}

std::string WideToUtf8(std::wstring const &value) {
  if (value.empty()) {
    return {};
  }
  if (value.size() > static_cast<size_t>((std::numeric_limits<int>::max)())) {
    throw std::invalid_argument("UTF-16 input is too large.");
  }
  int required = WideCharToMultiByte(
      CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
  if (required == 0) {
    throw WindowsError("WideCharToMultiByte");
  }
  std::string result(static_cast<size_t>(required), '\0');
  if (WideCharToMultiByte(
          CP_UTF8,
          WC_ERR_INVALID_CHARS,
          value.data(),
          static_cast<int>(value.size()),
          result.data(),
          required,
          nullptr,
          nullptr) == 0) {
    throw WindowsError("WideCharToMultiByte");
  }
  return result;
}

std::vector<DirectoryEntry> ListDirectory(std::string const &path) {
  auto directory = std::filesystem::path{Utf8ToWide(path)};
  std::error_code error;
  if (!std::filesystem::is_directory(directory, error)) {
    if (error) {
      throw std::filesystem::filesystem_error("Could not inspect directory", directory, error);
    }
    throw std::invalid_argument("Path is not a directory: " + path);
  }

  std::vector<DirectoryEntry> result;
  std::filesystem::directory_iterator iterator{
      directory, std::filesystem::directory_options::skip_permission_denied, error};
  if (error) {
    throw std::filesystem::filesystem_error("Could not enumerate directory", directory, error);
  }
  for (auto end = std::filesystem::directory_iterator{}; iterator != end; iterator.increment(error)) {
    if (error) {
      throw std::filesystem::filesystem_error("Could not continue enumerating directory", directory, error);
    }
    auto linkStatus = iterator->symlink_status(error);
    if (error) {
      error.clear();
      continue;
    }
    auto followedStatus = iterator->status(error);
    if (error) {
      error.clear();
      continue;
    }
    bool isDirectory = std::filesystem::is_directory(followedStatus);
    bool isFile = std::filesystem::is_regular_file(followedStatus);
    if (!isDirectory && !isFile) {
      continue;
    }
    auto absolutePath = AbsoluteNormalized(iterator->path());
    result.push_back({
        WideToUtf8(iterator->path().filename().wstring()),
        WideToUtf8(absolutePath.wstring()),
        isDirectory ? "directory" : "file",
        std::filesystem::is_symlink(linkStatus),
    });
  }
  std::sort(result.begin(), result.end(), [](DirectoryEntry const &left, DirectoryEntry const &right) {
    return _stricmp(left.name.c_str(), right.name.c_str()) < 0;
  });
  return result;
}

std::string ReadTextFile(std::string const &path) {
  auto filePath = std::filesystem::path{Utf8ToWide(path)};
  std::ifstream stream{filePath, std::ios::binary};
  if (!stream) {
    throw std::runtime_error("Could not open file for reading: " + path);
  }
  std::string contents{std::istreambuf_iterator<char>{stream}, std::istreambuf_iterator<char>{}};
  if (!stream.eof() && stream.fail()) {
    throw std::runtime_error("Could not read file: " + path);
  }
  if (contents.size() >= 3 && static_cast<unsigned char>(contents[0]) == 0xef &&
      static_cast<unsigned char>(contents[1]) == 0xbb && static_cast<unsigned char>(contents[2]) == 0xbf) {
    contents.erase(0, 3);
  }
  // Reject malformed input so metadata parsing never gets replacement bytes.
  Utf8ToWide(contents);
  return contents;
}

std::vector<uint8_t> ReadBinaryFile(std::string const &path) {
  auto filePath = std::filesystem::path{Utf8ToWide(path)};
  std::ifstream stream{filePath, std::ios::binary | std::ios::ate};
  if (!stream) {
    throw std::runtime_error("Could not open file for reading: " + path);
  }
  auto length = stream.tellg();
  if (length < 0 || static_cast<uint64_t>(length) > static_cast<uint64_t>((std::numeric_limits<size_t>::max)())) {
    throw std::runtime_error("File is too large to read: " + path);
  }
  std::vector<uint8_t> contents(static_cast<size_t>(length));
  stream.seekg(0, std::ios::beg);
  if (!contents.empty()) {
    stream.read(reinterpret_cast<char *>(contents.data()), static_cast<std::streamsize>(contents.size()));
  }
  if (!stream) {
    throw std::runtime_error("Could not read file: " + path);
  }
  return contents;
}

void WriteTextFile(std::string const &path, std::string const &contents) {
  Utf8ToWide(contents);
  auto destination = AbsoluteNormalized(std::filesystem::path{Utf8ToWide(path)});
  if (destination.filename().empty()) {
    throw std::invalid_argument("A text-file destination must include a filename.");
  }
  std::error_code error;
  std::filesystem::create_directories(destination.parent_path(), error);
  if (error) {
    throw std::filesystem::filesystem_error("Could not create the destination directory", destination.parent_path(), error);
  }
  auto temporary = destination;
  temporary += L".tmp";
  {
    std::ofstream stream{temporary, std::ios::binary | std::ios::trunc};
    if (!stream) {
      throw std::runtime_error("Could not open temporary text file.");
    }
    stream.write(contents.data(), static_cast<std::streamsize>(contents.size()));
    stream.flush();
    if (!stream) {
      throw std::runtime_error("Could not write temporary text file.");
    }
  }
  if (!MoveFileExW(
          temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
    auto failure = WindowsError("MoveFileExW");
    DeleteFileW(temporary.c_str());
    throw failure;
  }
}

std::string CanonicalizePath(std::string const &path) {
  auto input = std::filesystem::path{Utf8ToWide(path)};
  std::error_code error;
  auto canonical = std::filesystem::weakly_canonical(input, error);
  if (error) {
    canonical = AbsoluteNormalized(input);
  }
  return WideToUtf8(canonical.lexically_normal().wstring());
}

bool FileExists(std::string const &path) {
  std::error_code error;
  return std::filesystem::exists(std::filesystem::path{Utf8ToWide(path)}, error) && !error;
}

void OpenPath(std::string const &path) {
  auto target = AbsoluteNormalized(std::filesystem::path{Utf8ToWide(path)});
  std::error_code error;
  if (!std::filesystem::exists(target, error) || error) {
    throw std::invalid_argument("Path does not exist: " + path);
  }
  auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", target.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
  if (result <= 32) {
    throw std::runtime_error("Windows could not open the requested path (ShellExecute error " +
        std::to_string(static_cast<long long>(result)) + ").");
  }
}

std::vector<std::string> RemoveSaveDataDirectories(
    std::vector<std::string> const &paths,
    std::string const &titleId,
    bool confirmed) {
  if (!confirmed) {
    throw std::invalid_argument("Save-data deletion requires explicit confirmation.");
  }
  if (titleId.empty() || !std::all_of(titleId.begin(), titleId.end(), [](unsigned char character) {
        return std::isalnum(character) != 0 || character == '-' || character == '_';
      })) {
    throw std::invalid_argument("Save-data deletion requires a valid title id.");
  }

  auto expectedTitle = Utf8ToWide(titleId);
  std::vector<std::string> failed;
  for (auto const &path : paths) {
    try {
      auto input = std::filesystem::path{Utf8ToWide(path)};
      if (!input.is_absolute()) {
        throw std::invalid_argument("Save-data target must be absolute.");
      }
      auto target = AbsoluteNormalized(input);
      if (target == target.root_path() || target.filename().empty() ||
          _wcsicmp(target.filename().c_str(), expectedTitle.c_str()) != 0 ||
          _wcsicmp(target.parent_path().filename().c_str(), L"_SaveData") != 0) {
        throw std::invalid_argument("Save-data target is outside the exact _SaveData/title-id boundary.");
      }

      DWORD attributes = GetFileAttributesW(target.c_str());
      if (attributes == INVALID_FILE_ATTRIBUTES) {
        DWORD attributeError = GetLastError();
        if (attributeError == ERROR_FILE_NOT_FOUND || attributeError == ERROR_PATH_NOT_FOUND) {
          continue;
        }
        throw WindowsError("GetFileAttributesW", attributeError);
      }
      if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0 || (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        throw std::invalid_argument("Save-data target must be a non-reparse directory.");
      }
      DWORD parentAttributes = GetFileAttributesW(target.parent_path().c_str());
      if (parentAttributes == INVALID_FILE_ATTRIBUTES ||
          (parentAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
          (parentAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        throw std::invalid_argument("Save-data parent must be a non-reparse _SaveData directory.");
      }

      std::error_code error;
      std::filesystem::remove_all(target, error);
      if (error || std::filesystem::exists(target)) {
        throw std::filesystem::filesystem_error("Could not remove save-data directory", target, error);
      }
    } catch (...) {
      failed.push_back(path);
    }
  }
  return failed;
}

std::filesystem::path LauncherSettingsPath() {
  PWSTR localAppData = nullptr;
  auto result = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_CREATE, nullptr, &localAppData);
  if (FAILED(result)) {
    throw std::runtime_error("Could not resolve the current user's LocalAppData directory.");
  }
  std::filesystem::path path{localAppData};
  CoTaskMemFree(localAppData);
  return path / L"Prosperismo" / L"launcher-settings.json";
}

std::optional<std::string> LoadLauncherSettings() {
  auto path = LauncherSettingsPath();
  std::error_code error;
  if (!std::filesystem::exists(path, error)) {
    if (error) {
      throw std::filesystem::filesystem_error("Could not inspect launcher settings", path, error);
    }
    return std::nullopt;
  }
  return ReadTextFile(WideToUtf8(path.wstring()));
}

void SaveLauncherSettings(std::string const &json) {
  auto path = LauncherSettingsPath();
  WriteTextFile(WideToUtf8(path.wstring()), json);
}

std::optional<std::filesystem::path> FindEmulator() {
  constexpr wchar_t emulatorName[] = L"prosperismo_emulator.exe";
  std::vector<std::filesystem::path> candidates;
  auto appDirectory = ExecutableDirectory();
  AddCandidate(candidates, appDirectory / emulatorName);
  AddCandidate(candidates, appDirectory.parent_path() / emulatorName);

  std::error_code error;
  auto current = std::filesystem::current_path(error);
  if (!error) {
    AddCandidate(candidates, current / emulatorName);
    AddCandidate(candidates, current / L"artifacts" / L"Prosperismo-Windows-x64" / emulatorName);
  }

  auto ancestor = appDirectory;
  for (int depth = 0; depth != 8 && !ancestor.empty(); ++depth) {
    AddCandidate(candidates, ancestor / L"artifacts" / L"Prosperismo-Windows-x64" / emulatorName);
    auto parent = ancestor.parent_path();
    if (parent == ancestor) {
      break;
    }
    ancestor = std::move(parent);
  }

  for (auto const &candidate : candidates) {
    if (std::filesystem::is_regular_file(candidate, error) && !error) {
      return AbsoluteNormalized(candidate);
    }
    error.clear();
  }
  return std::nullopt;
}

std::wstring QuoteWindowsArgument(std::wstring const &argument) {
  if (!argument.empty() && argument.find_first_of(L" \t\n\v\"") == std::wstring::npos) {
    return argument;
  }

  std::wstring quoted{L'\"'};
  size_t backslashes = 0;
  for (wchar_t character : argument) {
    if (character == L'\\') {
      ++backslashes;
      continue;
    }
    if (character == L'\"') {
      quoted.append(backslashes * 2 + 1, L'\\');
      quoted.push_back(L'\"');
    } else {
      quoted.append(backslashes, L'\\');
      quoted.push_back(character);
    }
    backslashes = 0;
  }
  quoted.append(backslashes * 2, L'\\');
  quoted.push_back(L'\"');
  return quoted;
}

std::wstring BuildCommandLine(
    std::filesystem::path const &executable,
    std::vector<std::string> const &arguments) {
  std::wstring commandLine = QuoteWindowsArgument(executable.wstring());
  for (auto const &argument : arguments) {
    commandLine.push_back(L' ');
    commandLine.append(QuoteWindowsArgument(Utf8ToWide(argument)));
  }
  return commandLine;
}

void LaunchDetached(
    std::string const &executable,
    std::vector<std::string> const &arguments,
    std::string const &workingDirectory,
    std::function<void(std::optional<uint32_t>, std::string)> onExit) {
  auto executablePath = AbsoluteNormalized(std::filesystem::path{Utf8ToWide(executable)});
  std::error_code error;
  if (!std::filesystem::is_regular_file(executablePath, error) || error) {
    throw std::invalid_argument("Emulator executable does not exist: " + executable);
  }
  auto workingPath = AbsoluteNormalized(std::filesystem::path{Utf8ToWide(workingDirectory)});
  if (!std::filesystem::is_directory(workingPath, error) || error) {
    throw std::invalid_argument("Emulator working directory does not exist: " + workingDirectory);
  }

  auto commandLine = BuildCommandLine(executablePath, arguments);
  std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
  mutableCommandLine.push_back(L'\0');
  STARTUPINFOW startup{};
  startup.cb = sizeof(startup);
  PROCESS_INFORMATION process{};
  if (!CreateProcessW(
          executablePath.c_str(),
          mutableCommandLine.data(),
          nullptr,
          nullptr,
          FALSE,
          CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS | CREATE_UNICODE_ENVIRONMENT,
          nullptr,
          workingPath.c_str(),
          &startup,
          &process)) {
    throw WindowsError("CreateProcessW");
  }
  CloseHandle(process.hThread);
  std::thread([handle = process.hProcess, onExit = std::move(onExit)]() noexcept {
    auto waitResult = WaitForSingleObject(handle, INFINITE);
    DWORD exitCode = 0;
    bool readExitCode = waitResult == WAIT_OBJECT_0 && GetExitCodeProcess(handle, &exitCode);
    CloseHandle(handle);
    if (onExit) {
      if (readExitCode) {
        onExit(static_cast<uint32_t>(exitCode), {});
      } else {
        onExit(std::nullopt, "Windows could not observe the emulator process exit.");
      }
    }
  }).detach();
}

std::vector<std::string> ChooseGameDirectories() {
  HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
  if (FAILED(initialized)) {
    throw std::runtime_error("Could not initialize the folder picker COM apartment.");
  }
  struct ApartmentGuard {
    ~ApartmentGuard() { CoUninitialize(); }
  } apartmentGuard;

  IFileOpenDialog *dialog = nullptr;
  HRESULT result = CoCreateInstance(
      CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog));
  if (FAILED(result)) {
    throw std::runtime_error("Could not create the Windows folder picker.");
  }
  struct DialogGuard {
    IFileOpenDialog *value;
    ~DialogGuard() { value->Release(); }
  } dialogGuard{dialog};

  FILEOPENDIALOGOPTIONS options{};
  result = dialog->GetOptions(&options);
  if (FAILED(result) || FAILED(dialog->SetOptions(
                            options | FOS_PICKFOLDERS | FOS_ALLOWMULTISELECT | FOS_FORCEFILESYSTEM |
                                FOS_PATHMUSTEXIST))) {
    throw std::runtime_error("Could not configure the Windows folder picker.");
  }
  dialog->SetTitle(L"Choose Prosperismo game folders");
  result = dialog->Show(nullptr);
  if (result == HRESULT_FROM_WIN32(ERROR_CANCELLED)) {
    return {};
  }
  if (FAILED(result)) {
    throw std::runtime_error("The Windows folder picker failed.");
  }

  IShellItemArray *items = nullptr;
  result = dialog->GetResults(&items);
  if (FAILED(result)) {
    throw std::runtime_error("Could not read the selected game folders.");
  }
  struct ItemsGuard {
    IShellItemArray *value;
    ~ItemsGuard() { value->Release(); }
  } itemsGuard{items};

  DWORD count = 0;
  if (FAILED(items->GetCount(&count))) {
    throw std::runtime_error("Could not count the selected game folders.");
  }
  std::vector<std::string> paths;
  paths.reserve(count);
  for (DWORD index = 0; index < count; ++index) {
    IShellItem *item = nullptr;
    if (FAILED(items->GetItemAt(index, &item))) {
      throw std::runtime_error("Could not inspect a selected game folder.");
    }
    struct ItemGuard {
      IShellItem *value;
      ~ItemGuard() { value->Release(); }
    } itemGuard{item};
    auto selected = std::filesystem::path{ShellItemPath(item)};
    paths.push_back(WideToUtf8(std::filesystem::weakly_canonical(selected).wstring()));
  }
  return paths;
}

} // namespace prosperismo::host
