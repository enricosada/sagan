#!/usr/bin/env bash

set -eu
set -o pipefail

cd `dirname $0`

if [ ! -e ~/.config/.mono/certs ]
then
  mozroots --import --sync --quiet
fi

mono .paket/paket.bootstrapper.exe
mono .paket/paket.exe "$@"
