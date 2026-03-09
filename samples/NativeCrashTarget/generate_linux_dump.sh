#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "=== Building C++ NativeCrashTarget ==="
g++ -g -O0 -o native_crash_linux main.cpp -std=c++17
echo "Build OK"
echo

echo "=== Generating core dump via abort() ==="
# Enable core dumps and set core file pattern
ulimit -c unlimited

# Run the program — it will generate output then we'll send SIGABRT
./native_crash_linux --wait < /dev/null &
APP_PID=$!
sleep 1

echo
echo "Sending SIGABRT to PID $APP_PID..."
kill -ABRT $APP_PID 2>/dev/null || true
sleep 1

# Find core file 
CORE_FILE=""
for f in core core.$APP_PID /tmp/core.$APP_PID; do
    if [ -f "$f" ]; then
        CORE_FILE="$f"
        break
    fi
done

# Try using gcore as fallback — start fresh
if [ -z "$CORE_FILE" ]; then
    echo "No core file from abort. Trying gcore..."
    ./native_crash_linux --wait &
    APP_PID2=$!
    sleep 1
    gcore -o linux_core $APP_PID2 2>&1 || true
    kill $APP_PID2 2>/dev/null || true
    wait $APP_PID2 2>/dev/null || true
    CORE_FILE=$(ls -t linux_core.* 2>/dev/null | head -1)
fi

if [ -n "$CORE_FILE" ]; then
    echo
    echo "=== Core dump generated ==="
    ls -la "$CORE_FILE"
    FULL_PATH="$(cd "$(dirname "$CORE_FILE")" && pwd)/$(basename "$CORE_FILE")"
    echo
    echo "To debug with DebugMcpServer:"  
    echo "  load_dump_file(dumpPath: \"$FULL_PATH\", program: \"$(pwd)/native_crash_linux\", adapter: \"cpp\")"
else
    echo "ERROR: No core file generated. Core dumps may be disabled by the system."
    echo "Try: echo '/tmp/core.%p' | sudo tee /proc/sys/kernel/core_pattern"
    exit 1
fi
