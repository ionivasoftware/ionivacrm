#!/bin/bash
echo "🔍 Checking setup..."
echo ""

check() {
  if command -v $1 &> /dev/null; then
    echo "✅ $1: $(command $1 --version 2>&1 | head -1)"
  else
    echo "❌ $1: NOT FOUND"
  fi
}

check node
check python3
check git
check dotnet
check docker
check claude

echo ""
echo "📁 Checking folders..."
folders=("agents" "hooks" "input" "output" ".github/workflows" "logs")
for f in "${folders[@]}"; do
  if [ -d "$f" ]; then
    echo "✅ $f exists"
  else
    echo "❌ $f missing"
  fi
done

echo ""
echo "🔑 Checking .env..."
if [ -f ".env" ]; then
  echo "✅ .env exists"
  if [ $(stat -c %a .env) = "600" ]; then
    echo "✅ .env permissions correct (600)"
  else
    echo "⚠️  .env permissions wrong - run: chmod 600 .env"
  fi
else
  echo "❌ .env missing"
fi

echo ""
echo "🐍 Checking Python packages..."
packages=("claude_agent_sdk" "dotenv" "rich" "colorama")
source .venv/bin/activate 2>/dev/null
for p in "${packages[@]}"; do
  python3 -c "import $p" 2>/dev/null && echo "✅ $p" || echo "❌ $p missing"
done

echo ""
echo "Done! Fix any ❌ items above before proceeding."
