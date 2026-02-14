#!/usr/bin/env fish

# Script to run opencode with PROMPT.md content in a loop
# Press 'q' or Ctrl+C to stop

# Check if PROMPT.md exists
set prompt_file "PROMPT.md"

if not test -f $prompt_file
    echo "Error: $prompt_file not found in current directory"
    echo "Looking in script directory..."

    # Try the script's directory
    set script_dir (dirname (status -f))
    set prompt_file "$script_dir/PROMPT.md"

    if not test -f $prompt_file
        echo "Error: PROMPT.md not found"
        exit 1
    end
end

# Read the content of PROMPT.md
if not set prompt_content (cat $prompt_file 2>/dev/null)
    echo "Error: Failed to read $prompt_file"
    exit 1
end

# Check if prompt is empty
if test -z "$prompt_content"
    echo "Error: PROMPT.md is empty"
    exit 1
end

# Track the opencode PID for cleanup
set -g opencode_pid ""

# Global flag to control the loop
set -g running true

# Signal handler for SIGINT (Ctrl+C)
function handle_sigint --on-signal SIGINT -V opencode_pid
    echo ""
    echo "Caught Ctrl+C, stopping..."

    set -g running false

    if test -n "$opencode_pid"
        echo "Killing opencode (PID: $opencode_pid)..."
        kill -TERM $opencode_pid 2>/dev/null
        sleep 0.3
        if kill -0 $opencode_pid 2>/dev/null
            kill -KILL $opencode_pid 2>/dev/null
        end
    end

    echo "Done."
    exit 130
end

# Function to kill any running opencode process
function kill_opencode -V opencode_pid
    if test -n "$opencode_pid"
        kill -TERM $opencode_pid 2>/dev/null
        sleep 0.3
        if kill -0 $opencode_pid 2>/dev/null
            kill -KILL $opencode_pid 2>/dev/null
        end
        set -g opencode_pid ""
    end
end

echo "Running opencode in a loop with prompt from: $prompt_file"
echo "Press 'q' + Enter to stop, or Ctrl+C to force stop"
echo "---"

set iteration 1

while $running
    echo ""
    echo "=== Iteration $iteration ==="
    echo ""

    # Run opencode with the prompt content
    opencode run $prompt_content &
    set opencode_pid $last_pid

    # Wait for opencode to complete
    wait $opencode_pid
    set exit_code $status
    set opencode_pid ""

    echo ""
    echo "Iteration $iteration completed with exit code: $exit_code"

    # Check if we should stop (non-blocking read with timeout)
    # Using stty and read to check for keypress
    if not $running
        break
    end

    # Small delay between iterations
    echo "Starting next iteration in 2 seconds... (press 'q' + Enter to stop)"
    sleep 2

    set iteration (math $iteration + 1)
end

echo ""
echo "Loop stopped. Total iterations: "(math $iteration - 1)
exit 0
