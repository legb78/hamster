#!/bin/sh
# Hook Notification de Claude Code pour le hamster.
# Recopie tel quel le JSON recu sur stdin dans ~/.hamster/events.jsonl, que le hamster
# surveille. Ne doit jamais gener Claude : aucune sortie, et code 0 quoi qu'il arrive.
exec 2>/dev/null
dir="$HOME/.hamster"
mkdir -p "$dir" || exit 0
{ cat; printf '\n'; } >> "$dir/events.jsonl"
exit 0
