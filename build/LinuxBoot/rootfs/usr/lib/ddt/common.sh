# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# What the start scripts of the DDT Linux boot environment share.

PATH=/usr/bin
export PATH

# From this call on, the second screen and the serial port give a root shell.
open_shell() {
    : > /run/ddt/shell-open
}

shell_is_open() {
    [ -e /run/ddt/shell-open ]
}

# A line for the kernel's log alone, which dmesg shows.
note() {
    echo "<6>ddt: $1" > /dev/kmsg
}

# One line per stage, on the console and in the kernel's log. A machine may have no console that opens.
stage() {
    { echo "ddt: $1" > /dev/console; } 2> /dev/null
    note "$1"
}

# Prints the value of name=value on the kernel command line.
argument() {
    read -r arguments < /proc/cmdline
    set -f
    for word in $arguments; do
        case $word in
            "$1"=*)
                set +f
                echo "${word#*=}"
                return 0
                ;;
        esac
    done
    set +f
    return 1
}

# Whether the kernel command line has the word.
has_argument() {
    read -r arguments < /proc/cmdline
    set -f
    for word in $arguments; do
        if [ "$word" = "$1" ]; then
            set +f
            return 0
        fi
    done
    set +f
    return 1
}
