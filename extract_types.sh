#!/bin/bash
set -e

# Helper: extract a type from a file into its own file
# Usage: extract_type <source_file> <type_declaration_line> <new_file> <namespace>

extract_after_class() {
    local src="$1" newfile="$2" ns="$3" typename="$4"
    # This is handled manually per type below
    echo "Extracting $typename from $src to $newfile"
}

# 1. EToolResult already done

# 2. VectorMemoryStore → VectorEntry + VectorSearchResult
echo "=== VectorMemoryStore ==="
grep -n "class VectorEntry\|class VectorSearchResult" Memory/VectorMemoryStore.cs
