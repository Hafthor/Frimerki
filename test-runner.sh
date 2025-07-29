#!/bin/bash

echo "Running tests multiple times to reproduce intermittent failure..."

for i in {1..5}; do
    echo "=== Test Run #$i ==="
    dotnet test --no-build --logger "console;verbosity=minimal"

    if [ $? -ne 0 ]; then
        echo "❌ FAILURE DETECTED on run #$i"
        break
    else
        echo "✅ Run #$i passed"
    fi

    echo ""
done

echo "Test run complete."
