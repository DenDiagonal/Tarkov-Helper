import os

base_dir = r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper"

def replace_in_file(rel_path, replacements):
    path = os.path.join(base_dir, rel_path)
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    for k, v in replacements.items():
        if k not in content:
            print(f"Warning: '{k}' not found in {rel_path}")
        content = content.replace(k, v)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"Updated {rel_path}")

# QuestRecommendationService.cs
replace_in_file(r"Services\QuestRecommendationService.cs", {
    'return $"필요 아이템 {readyItems.Count}개 보유 중";': 'return $"Have {readyItems.Count} required items";',
    'return "지금 바로 완료 가능";': 'return "Ready to complete now";',
    'return $"아이템 제출만 필요 ({ready.Count}/{total}개 보유)";': 'return $"Item turn-in only ({ready.Count}/{total} owned)";',
    'return $"카파 필수 + {unlocksCount}개 퀘스트 해금";': 'return $"Kappa required + unlocks {unlocksCount} quests";',
    'return "카파 컨테이너 필수 퀘스트";': 'return "Required for Kappa Container";',
    'return $"{unlocksCount}개 퀘스트 해금";': 'return $"Unlocks {unlocksCount} quests";',
    'return "아이템 필요 없음";': 'return "No items required";'
})

# DatabaseUpdateService.cs
replace_in_file(r"Services\DatabaseUpdateService.cs", {
    '"업데이트 기능이 비활성화되었습니다"': '"Update feature is disabled"',
    '"원격 버전을 가져오지 못했습니다"': '"Failed to fetch remote version"',
    '"데이터베이스가 최신 버전입니다"': '"Database is up to date"',
    '"데이터베이스 다운로드에 실패했습니다"': '"Failed to download database"',
    '$"버전 {remoteVersion}(으)로 업데이트되었습니다"': '$"Updated to version {remoteVersion}"'
})

# LogSyncService.cs
replace_in_file(r"Services\LogSyncService.cs", {
    'progress?.Report($"로그 파일 {logFiles.Count}개를 찾았습니다");': 'progress?.Report($"Found {logFiles.Count} log files");',
    'progress?.Report($"{processed}/{logFiles.Count}개 파일 분석 중 ({allEvents.Count}개 이벤트)");': 'progress?.Report($"Analyzing {processed}/{logFiles.Count} files ({allEvents.Count} events)...");',
    'progress?.Report("로그 파일 스캔 중...");': 'progress?.Report("Scanning log files...");',
    'progress?.Report($"최근 {daysRange}일 동안의 이벤트 {events.Count}/{originalCount}개로 필터링됨");': 'progress?.Report($"Filtered to {events.Count}/{originalCount} events from last {daysRange} days");',
    'result.Errors.Add("로그에서 퀘스트 이벤트를 찾을 수 없습니다");': 'result.Errors.Add("No quest events found in logs");',
    'progress?.Report($"{events.Count}개의 퀘스트 이벤트 분석 중...");': 'progress?.Report($"Analyzing {events.Count} quest events...");',
    'progress?.Report($"업데이트할 퀘스트 {questsToComplete.Count}개를 찾았습니다");': 'progress?.Report($"Found {questsToComplete.Count} quests to update");'
})

# ScreenshotWatcherService.cs
replace_in_file(r"Services\Map\ScreenshotWatcherService.cs", {
    'OnError($"폴더가 존재하지 않습니다: {folderPath}");': 'OnError($"Folder does not exist: {folderPath}");',
    'OnError($"감시 시작 실패: {ex.Message}");': 'OnError($"Failed to start screenshot watcher: {ex.Message}");',
    'OnError($"파일 접근 불가: {fileName}");': 'OnError($"Cannot access file: {fileName}");',
    'OnParsingFailed(fileName, "패턴이 일치하지 않습니다");': 'OnParsingFailed(fileName, "Pattern does not match");',
    'OnError($"파일 처리 오류 ({fileName}): {ex.Message}");': 'OnError($"File processing error ({fileName}): {ex.Message}");',
    'OnError($"FileSystemWatcher 오류: {e.GetException().Message}");': 'OnError($"FileSystemWatcher error: {e.GetException().Message}");'
})

# AutoCalibrationService.cs
replace_in_file(r"Services\Map\AutoCalibrationService.cs", {
    'result.ErrorMessage = "보정 포인트가 3개 이상 필요합니다.";': 'result.ErrorMessage = "At least 3 calibration points are required.";',
    'result.ErrorMessage = "유효한 대응점이 3개 미만입니다.";': 'result.ErrorMessage = "Fewer than 3 valid corresponding points found.";',
    'result.ErrorMessage = "매핑 계산 실패";': 'result.ErrorMessage = "Mapping calculation failed";'
})

# LogMapWatcherService.cs
replace_in_file(r"Services\Map\LogMapWatcherService.cs", {
    'ErrorOccurred?.Invoke(this, "로그 폴더가 설정되지 않았거나 존재하지 않습니다.");': 'ErrorOccurred?.Invoke(this, "Log folder is not set or does not exist.");',
    'ErrorOccurred?.Invoke(this, $"로그 감시 시작 실패: {ex.Message}");': 'ErrorOccurred?.Invoke(this, $"Failed to start log watcher: {ex.Message}");'
})

print("Services translated successfully!")
