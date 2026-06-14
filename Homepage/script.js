(function () {
  const releaseUrl = "https://github.com/aihenry2980/Jx_SourceGit_Win11/releases/latest";
  const languageStorageKey = "jx_sourcegit_home_language";

  const translations = {
    zh: {
      metaDescription: "Jx SourceGit Win11 fork: 一个带机械控制台气质、面向 Windows 工作流的 SourceGit fork。",
      navFork: "Fork",
      navFeatures: "Features",
      navScreenshots: "Screenshots",
      navDownload: "Download",
      heroEyebrow: "forked git workbench / windows x64",
      heroText: "一个带机械控制台气质的 SourceGit fork：面向复杂分支、递归 submodule、SPP 指针、快速 fetch 和 Windows 日常维护流程做了大量细节增强。",
      heroDownload: "前往 GitHub Releases 下载",
      heroSource: "查看源码仓库",
      statusBaseLabel: "BASE",
      statusBaseValue: "SourceGit fork",
      statusBranchLabel: "BRANCH",
      statusChannelLabel: "CHANNEL",
      statusFocusLabel: "FOCUS",
      statusFocusValue: "Submodule ops",
      forkEyebrow: "fork notice",
      forkTitle: "这是对原始 SourceGit 项目的个人 fork",
      forkBody: "本工具基于原始开源项目 <a href=\"https://github.com/sourcegit-scm/sourcegit\">sourcegit-scm/sourcegit</a>。原始作品的核心设计、版权和贡献归原作者及贡献者所有。本 fork 不是 upstream 官方发行版，主要用于加入我自己的 Windows 仓库维护、分支筛选、history graph 和递归 submodule 工作流增强。",
      featuresEyebrow: "feature ledger from commit history",
      featuresTitle: "从 commit message 整理出来的增强清单",
      featuresIntro: "下面这些点来自本 fork 相对 upstream 的提交记录，按日常使用场景归类。它不是空泛的 feature wall，更像一张维修台上的改装记录。",
      featureSubmoduleTitle: "递归 submodule 和 SPP 工作流",
      featureSubmodule1: "递归 submodule update 的顺序、弹窗、结果报告和状态 UX 做了多轮打磨。",
      featureSubmodule2: "递归 local changes view、递归 clean action、nested file details 更适合维护大型嵌套仓库。",
      featureSubmodule3: "submodule diff summary 更清楚，丢失 revision 时禁用 Open Details，减少误点。",
      featureSubmodule4: "围绕 super-project pointer 做过滤、标记和图谱提示，便于追踪 submodule 指针变化。",
      featureSubmodule5: "AvaloniaEdit submodule 保持使用个人 fork，并吸收 upstream 需要的变更。",
      featureHistoryTitle: "History graph、refs 和拖拽操作",
      featureHistory1: "改进 history graph change indicators、graph markers、commit summary 和 graph alignment。",
      featureHistory2: "增强 refs/tag 显示、bookmark 颜色、history tag toggle 和小屏幕可用性。",
      featureHistory3: "commit subject 和 refs 支持拖拽动作，带 Rebase / Hard Reset 的指针反馈和取消清理。",
      featureHistory4: "支持 Ctrl + wheel 缩放 history 窗口，历史空状态信息更明确。",
      featureHistory5: "关闭 debug instance 时对 graph generation 加保护，避免 UIStates 为空导致异常。",
      featureBranchTitle: "分支过滤、颜色和上下文菜单",
      featureBranch1: "Preset Branch Filter 支持 exact names、contains patterns、exclude names。",
      featureBranch2: "修复 contains pattern 自动补全问题，并修复 exact names chip 在输入框中被截断的问题。",
      featureBranch3: "跨 repo 对比 matching remotes，远程分支 context menu 更易读。",
      featureBranch4: "分支 context menu 增加更快的颜色和 push 操作路径。",
      featureBranch5: "Push 流程里的 remote branch 自动选择和 branch selector 更适合多远程仓库。",
      featureFetchTitle: "Fetch、LFS 和命令流程反馈",
      featureFetch1: "Quick fetch modes、dark toolbar theme 和 fetch feedback 有单独增强。",
      featureFetch2: "修复 LFS 场景可能卡住的问题，并收紧 auto-fetch state handling。",
      featureFetch3: "命令工作流、递归操作、Actions popup 行为和 countdown 反馈更直观。",
      featureFetch4: "窄窗口下减少 toolbar 按钮宽度和间距，避免工具栏图标互相挤压。",
      featureFetch5: "增加 remote git address text，查看仓库来源更直接。",
      featureDiffTitle: "Diff、local changes 和冲突处理",
      featureDiff1: "submodule diff summaries 和 nested file details 帮助理解指针与文件层级变化。",
      featureDiff2: "local changes layout、diff 交互和 branch filter 交互做了 polish。",
      featureDiff3: "支持配置 pull-related conflicts 时自动 revert 的文件扩展名。",
      featureDiff4: "保留 SourceGit 的 text diff、stash、rebase、worktree 基础能力，同时针对本 fork 工作流补强。",
      featureDiff5: "局部修复和防错逻辑减少误操作后的状态不一致。",
      featureWindowsTitle: "Windows 发布、诊断和启动器",
      featureWindows1: "增加 Windows self-update install workflow 和 release cmd script。",
      featureWindows2: "release workflow 限制到 Windows x64，减少不需要的平台产物。",
      featureWindows3: "增加 memory profiling window，用来定位打开的仓库和 UI 功能的内存占用。",
      featureWindows4: "Launcher title bar、+Folder shortcut 和仓库侧栏布局更贴近日常启动流程。",
      featureWindows5: "增加“do not close window”方向的窗口保护尝试，以及更多小型按钮和操作入口。",
      screenshotsEyebrow: "screenshots",
      screenshotsTitle: "界面截图",
      shotDarkAlt: "Jx SourceGit Win11 深色主题截图",
      shotDarkCaption: "Dark theme / history workbench",
      shotLightAlt: "Jx SourceGit Win11 浅色主题截图",
      shotLightCaption: "Light theme / repository dashboard",
      downloadEyebrow: "release hatch",
      downloadTitle: "下载最新版本",
      downloadText: "安装包和发布说明放在 GitHub Releases。页面上的下载点击会被记录为 download_click 事件。",
      downloadButton: "打开 Releases",
      footerLabel: "Jx SourceGit Win11 fork",
      footerOriginal: "Original SourceGit",
      footerRepo: "Fork repository"
    },
    en: {
      metaDescription: "Jx SourceGit Win11 fork: a mechanical, Windows-focused Git GUI based on SourceGit.",
      navFork: "Fork",
      navFeatures: "Features",
      navScreenshots: "Screenshots",
      navDownload: "Download",
      heroEyebrow: "forked git workbench / windows x64",
      heroText: "A mechanical-console-flavored SourceGit fork tuned for complex branches, recursive submodules, SPP pointers, quick fetch, and everyday Windows repository maintenance.",
      heroDownload: "Download from GitHub Releases",
      heroSource: "View source repository",
      statusBaseLabel: "BASE",
      statusBaseValue: "SourceGit fork",
      statusBranchLabel: "BRANCH",
      statusChannelLabel: "CHANNEL",
      statusFocusLabel: "FOCUS",
      statusFocusValue: "Submodule ops",
      forkEyebrow: "fork notice",
      forkTitle: "A personal fork of the original SourceGit project",
      forkBody: "This tool is based on the original open-source project <a href=\"https://github.com/sourcegit-scm/sourcegit\">sourcegit-scm/sourcegit</a>. The original design, copyright, and contributions belong to the original authors and contributors. This fork is not an official upstream release; it adds my own Windows repository maintenance, branch filtering, history graph, and recursive submodule workflow enhancements.",
      featuresEyebrow: "feature ledger from commit history",
      featuresTitle: "Enhancement list distilled from commit messages",
      featuresIntro: "These items come from this fork's commits relative to upstream and are grouped by daily workflow. It is less a generic feature wall and more a modification log from the workbench.",
      featureSubmoduleTitle: "Recursive submodule and SPP workflow",
      featureSubmodule1: "Recursive submodule update order, dialogs, result reporting, and status UX have been refined across several passes.",
      featureSubmodule2: "Recursive local changes view, recursive clean action, and nested file details are better suited for large nested repositories.",
      featureSubmodule3: "Submodule diff summaries are clearer, and Open Details is disabled when one revision is missing to reduce misclicks.",
      featureSubmodule4: "Filtering, markers, and graph hints around super-project pointers make submodule pointer changes easier to trace.",
      featureSubmodule5: "The AvaloniaEdit submodule stays on the personal fork while absorbing needed upstream changes.",
      featureHistoryTitle: "History graph, refs, and drag operations",
      featureHistory1: "History graph change indicators, graph markers, commit summaries, and graph alignment have been improved.",
      featureHistory2: "Refs/tag display, bookmark colors, history tag toggles, and small-screen usability were strengthened.",
      featureHistory3: "Commit subjects and refs support drag actions with Rebase / Hard Reset pointer feedback and cleanup on cancel.",
      featureHistory4: "Ctrl + wheel zoom is supported in the history window, with clearer empty-state information.",
      featureHistory5: "Graph generation is guarded while closing a debug instance to avoid null UI state exceptions.",
      featureBranchTitle: "Branch filtering, colors, and context menus",
      featureBranch1: "Preset Branch Filter supports exact names, contains patterns, and exclude names.",
      featureBranch2: "Contains-pattern autocomplete was fixed, and exact-name chips no longer get clipped inside the input.",
      featureBranch3: "Cross-repository comparison for matching remotes and remote branch context menus are easier to read.",
      featureBranch4: "Branch context menus include faster paths for color and push actions.",
      featureBranch5: "Remote branch auto-selection and the branch selector in Push flows work better for multi-remote repositories.",
      featureFetchTitle: "Fetch, LFS, and command feedback",
      featureFetch1: "Quick fetch modes, dark toolbar theming, and fetch feedback received focused improvements.",
      featureFetch2: "Possible LFS hangs were fixed, and auto-fetch state handling was tightened.",
      featureFetch3: "Command workflows, recursive operations, Actions popup behavior, and countdown feedback are more direct.",
      featureFetch4: "Toolbar button widths and gaps shrink on narrow windows to prevent icon overlap.",
      featureFetch5: "Remote git address text makes repository origin easier to inspect.",
      featureDiffTitle: "Diff, local changes, and conflict handling",
      featureDiff1: "Submodule diff summaries and nested file details help explain pointer and file hierarchy changes.",
      featureDiff2: "Local changes layout, diff interactions, and branch filter interactions were polished.",
      featureDiff3: "A preference can auto-revert configured file extensions when pull-related conflicts occur.",
      featureDiff4: "SourceGit's text diff, stash, rebase, and worktree basics remain, with fork-specific workflow reinforcement.",
      featureDiff5: "Targeted fixes and guardrails reduce inconsistent states after risky operations.",
      featureWindowsTitle: "Windows release, diagnostics, and launcher",
      featureWindows1: "Windows self-update install workflow and release command scripts were added.",
      featureWindows2: "Release workflows are limited to Windows x64 to reduce unnecessary artifacts.",
      featureWindows3: "A memory profiling window helps identify which repositories and UI features consume memory.",
      featureWindows4: "Launcher title bar, +Folder shortcut, and repository sidebar layout better match daily startup flow.",
      featureWindows5: "Window-protection experiments and additional compact buttons add more maintenance entry points.",
      screenshotsEyebrow: "screenshots",
      screenshotsTitle: "Interface screenshots",
      shotDarkAlt: "Jx SourceGit Win11 dark theme screenshot",
      shotDarkCaption: "Dark theme / history workbench",
      shotLightAlt: "Jx SourceGit Win11 light theme screenshot",
      shotLightCaption: "Light theme / repository dashboard",
      downloadEyebrow: "release hatch",
      downloadTitle: "Download the latest version",
      downloadText: "Installers and release notes live on GitHub Releases. Download clicks on this page are recorded as download_click events.",
      downloadButton: "Open Releases",
      footerLabel: "Jx SourceGit Win11 fork",
      footerOriginal: "Original SourceGit",
      footerRepo: "Fork repository"
    }
  };

  function getSessionId() {
    const key = "jx_sourcegit_home_session";
    const value = `${Date.now()}-${Math.random().toString(16).slice(2)}`;

    try {
      const existing = window.sessionStorage.getItem(key);
      if (existing) {
        return existing;
      }

      window.sessionStorage.setItem(key, value);
    } catch {
      return value;
    }

    return value;
  }

  function track(eventName, extra) {
    const config = window.JX_SOURCEGIT_ANALYTICS || {};
    const endpoint = config.googleAppsScriptUrl;
    if (!endpoint || endpoint.includes("PASTE_YOUR")) {
      return;
    }

    const payload = {
      event: eventName,
      project: "Jx SourceGit Win11",
      page: window.location.pathname,
      title: document.title,
      url: window.location.href,
      referrer: document.referrer,
      language: navigator.language,
      userAgent: navigator.userAgent,
      screen: `${window.screen.width}x${window.screen.height}`,
      sessionId: getSessionId(),
      timestamp: new Date().toISOString(),
      ...extra
    };

    try {
      navigator.sendBeacon?.(endpoint, new Blob([JSON.stringify(payload)], { type: "text/plain" })) ||
        fetch(endpoint, {
          method: "POST",
          mode: "no-cors",
          keepalive: true,
          headers: { "Content-Type": "text/plain" },
          body: JSON.stringify(payload)
        });
    } catch {
      // Analytics must never block navigation.
    }
  }

  function getSavedLanguage() {
    try {
      const saved = window.localStorage.getItem(languageStorageKey);
      if (saved === "en" || saved === "zh") {
        return saved;
      }
    } catch {
      return "zh";
    }

    return "zh";
  }

  function setSavedLanguage(language) {
    try {
      window.localStorage.setItem(languageStorageKey, language);
    } catch {
      // Language switching should still work when storage is unavailable.
    }
  }

  function applyLanguage(language) {
    const dictionary = translations[language] || translations.zh;
    document.documentElement.lang = language === "en" ? "en" : "zh-CN";
    document.querySelector('meta[name="description"]')?.setAttribute("content", dictionary.metaDescription);

    document.querySelectorAll("[data-i18n]").forEach((element) => {
      const key = element.dataset.i18n;
      if (dictionary[key]) {
        element.innerHTML = dictionary[key];
      }
    });

    document.querySelectorAll("[data-i18n-alt]").forEach((element) => {
      const key = element.dataset.i18nAlt;
      if (dictionary[key]) {
        element.setAttribute("alt", dictionary[key]);
      }
    });

    document.querySelectorAll("[data-lang]").forEach((button) => {
      const active = button.dataset.lang === language;
      button.classList.toggle("active", active);
      if (active) {
        button.setAttribute("aria-current", "true");
      } else {
        button.removeAttribute("aria-current");
      }
    });
  }

  function bindLanguageSwitch() {
    document.querySelectorAll("[data-lang]").forEach((button) => {
      button.addEventListener("click", () => {
        const language = button.dataset.lang;
        if (language !== "en" && language !== "zh") {
          return;
        }

        setSavedLanguage(language);
        applyLanguage(language);
      });
    });
  }

  window.addEventListener("DOMContentLoaded", () => {
    applyLanguage(getSavedLanguage());
    bindLanguageSwitch();
    track("page_view");

    document.querySelectorAll("[data-track-download]").forEach((link) => {
      link.addEventListener("click", () => {
        track("download_click", {
          targetUrl: link.href || releaseUrl
        });
      });
    });
  });
})();
