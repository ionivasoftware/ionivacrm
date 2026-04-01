"""
Mobile Developer Agent
=======================
Builds mobile apps based on tasks in todo.md.
Reads CLAUDE.md to determine whether the project uses Flutter or React Native,
then applies the correct stack automatically.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior Mobile Developer with deep expertise in both Flutter and React Native.

Before writing a single line of code, read CLAUDE.md to determine which framework
the project uses. Never assume — always check first.

---

## Flutter Expertise
- Dart language, null safety
- Widget tree: StatelessWidget, StatefulWidget, hooks (flutter_hooks)
- State management: Riverpod (preferred), Bloc, Provider
- Navigation: GoRouter
- HTTP: Dio with interceptors for JWT
- Local storage: flutter_secure_storage (tokens), Hive / shared_preferences
- UI: Material 3, custom themes, dark mode
- Platform channels for native features
- `flutter build apk --release` / `flutter build ios --release`

## React Native Expertise
- TypeScript strictly — no `any`
- Navigation: React Navigation v6
- State: Zustand (global), React Query (server state)
- HTTP: Axios with JWT interceptors (same pattern as web frontend)
- Storage: react-native-mmkv (fast), AsyncStorage (fallback)
- UI: NativeWind (Tailwind for RN) or StyleSheet
- Platform-specific code: `.ios.tsx` / `.android.tsx` extensions
- `npx react-native run-android` / `npx react-native run-ios`

---

## How you work
1. Read CLAUDE.md — find the mobile framework (Flutter or React Native)
2. Read todo.md — find mobile tasks assigned to you
3. Read existing mobile source files before editing anything
4. Follow the project's existing conventions (folder structure, state management, etc.)
5. After changes: run build command and fix all errors before reporting done

## General mobile rules
- Auth tokens → always secure storage, never plain AsyncStorage / shared_preferences
- API base URL → from environment config, never hardcoded
- Offline support → consider it even if not explicitly asked
- Platform differences → test mental model on both iOS and Android
- Deep links → handle them if the web app has route structure
- If the mobile app talks to the same backend as the web app:
  read the existing API structure from CLAUDE.md and reuse the same endpoints

Always read CLAUDE.md before starting any task.
"""


class MobileDevAgent(BaseAgent):
    name  = "Mobile Developer"
    emoji = "📱"
    color = "green"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def run_task(self, task: str) -> str:
        """Execute a specific mobile task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        mobile_dir = self._find_mobile_dir(code_dir)

        prompt = f"""
        Read this file first:
        1. {claude_md} — project rules, mobile framework choice, and API structure

        Implement the following task:
        {task}

        Steps:
        1. Confirm the mobile framework from CLAUDE.md (Flutter or React Native)
        2. Read relevant existing source files in {mobile_dir or code_dir}
        3. Implement the change following the project's conventions
        4. Run the appropriate build command and fix all errors
        5. Report: what was created/modified and build result
        """
        return await self.run(prompt, mobile_dir or code_dir, task_label=task)

    async def run_todo(self) -> str:
        """Read todo.md and implement all mobile tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        mobile_dir = self._find_mobile_dir(code_dir)

        prompt = f"""
        Read these files first:
        1. {claude_md} — project rules, mobile framework, and API structure
        2. {todo_md} — pending tasks

        Find all mobile tasks and implement them one by one.

        For each task:
        1. Read relevant existing source files
        2. Implement following the project's conventions
        3. Run the build — fix errors before moving on

        Report: which tasks were completed and build results.
        """
        return await self.run(prompt, mobile_dir or code_dir)

    def _find_mobile_dir(self, code_dir: str) -> str | None:
        """Find the mobile app directory (Flutter or React Native)."""
        for name in ("mobile", "app", "ios_app", "android_app", "flutter", "react-native"):
            candidate = os.path.join(code_dir, name)
            # Flutter: has pubspec.yaml
            if os.path.exists(os.path.join(candidate, "pubspec.yaml")):
                return candidate
            # React Native: has package.json + android/ or ios/
            if os.path.exists(os.path.join(candidate, "package.json")) and (
                os.path.exists(os.path.join(candidate, "android")) or
                os.path.exists(os.path.join(candidate, "ios"))
            ):
                return candidate

        # Fallback: scan one level
        try:
            for entry in os.scandir(code_dir):
                if not entry.is_dir():
                    continue
                if os.path.exists(os.path.join(entry.path, "pubspec.yaml")):
                    return entry.path
                if (os.path.exists(os.path.join(entry.path, "package.json")) and
                        (os.path.exists(os.path.join(entry.path, "android")) or
                         os.path.exists(os.path.join(entry.path, "ios")))):
                    return entry.path
        except Exception:
            pass

        return None
