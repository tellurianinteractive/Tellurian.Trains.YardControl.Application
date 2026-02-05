#!/bin/bash

# Check if mcp-proxy is installed, if not install it
if ! command -v mcp-proxy &> /dev/null; then
    echo "mcp-proxy not found, installing..." >&2
    uv tool install mcp-proxy
fi

# Get the port from environment variable, default to 37140 if not set
if [ ! -z "${LSP_MCP_PORT}" ]; then
    PORT="${LSP_MCP_PORT}"
else
    # Look for a file in the current dir called .lsp_mcp.port and get the port from there
    if [ -f .lsp_mcp.port ]; then
        PORT=$(cat .lsp_mcp.port)
    else
        PORT=37140
    fi
fi

# Call mcp-proxy to connect to the LSP server
exec mcp-proxy --transport streamablehttp "http://localhost:${PORT}"
