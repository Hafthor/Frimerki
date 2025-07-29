#!/bin/bash
#
# Format and check script for Frímerki Email Server
# This script formats the code and then verifies it's properly formatted
#

echo "🔧 Formatting C# code..."
dotnet format --verbosity normal

format_exit_code=$?

if [ $format_exit_code -ne 0 ]; then
    echo ""
    echo "❌ Code formatting failed!"
    echo "Please check the output above for errors."
    exit 1
fi

echo ""
echo "✅ Code formatting completed successfully!"
echo ""
echo "🔍 Running build to ensure everything compiles..."
dotnet build --verbosity minimal

build_exit_code=$?

if [ $build_exit_code -ne 0 ]; then
    echo ""
    echo "❌ Build failed after formatting!"
    echo "Please fix the build errors above."
    exit 1
fi

echo ""
echo "✅ Build successful!"
echo ""
echo "🧪 Running tests to ensure functionality is preserved..."
dotnet test --verbosity minimal

test_exit_code=$?

if [ $test_exit_code -ne 0 ]; then
    echo ""
    echo "❌ Tests failed!"
    echo "Please fix the failing tests above."
    exit 1
fi

echo ""
echo "🎉 All checks passed! Your code is ready to commit."
echo ""
echo "Summary:"
echo "  ✅ Code formatted"
echo "  ✅ Build successful"
echo "  ✅ All tests passing"
echo ""
