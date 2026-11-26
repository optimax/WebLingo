#!/usr/bin/env bash
# Serves Examples.wasp/example1 on this machine, Linux on x64, as CI's ubuntu-latest runs it, with WebLingo installed from
# the archive package.sh made into <folder>: through wasp fastcgi behind nginx, and behind Apache httpd through
# mod_proxy_fcgi, as the WASP User Manual's section 18 configures them, and through mod_fcgid, which starts wasp itself;
# each page of the example asked for through each, its text checked. nginx, Apache httpd and mod_fcgid come from the
# distribution's packages, run by this account, each on a port of its own, listening on 127.0.0.1 only, and stopped.
# Needs sudo, to install the packages and WebLingo.
#   Installers/Linux/tests/serve.sh <folder>
set -euo pipefail
packages=$(cd "$1" && pwd)
repository=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
work=$(mktemp -d)
pids=()
stop() {
    for pid in "${pids[@]}"; do kill "$pid" 2> /dev/null || true; done
    wait 2> /dev/null || true
    rm -rf "$work"
}
trap stop EXIT
fail() { echo "FAILED: $*" >&2; for log in "$work"/*.log; do [ -f "$log" ] && { echo "== $log"; cat "$log"; } >&2; done; exit 1; }

sudo apt-get update -qq > /dev/null
sudo apt-get install -y -qq nginx apache2 libapache2-mod-fcgid > /dev/null
tar -xzf "$(ls "$packages"/weblingo-*-linux-x64.tar.gz)" -C "$work"
sudo "$work"/weblingo-*-linux-x64/install.sh > /dev/null
wasp=/opt/weblingo/wasp
site=$work/site
cp -R "$repository/Examples.wasp/example1" "$site"

free() { python3 -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()'; }

# The example's pages, as WaspServeTests checks them, and what it never serves.
pages() {
    local port=$1 how=$2
    for check in "/|<h3>Darjeeling</h3>" "/teas|Brew at 90 °C (194 °F) for 3 minutes." \
                 "/brewing|<tr><td>Assam</td><td>100 °C, 212 °F</td><td>4 minutes</td></tr>" \
                 "/about|Tea &amp; patience &lt;always&gt;" "/css/site.css|--leaf"; do
        local path=${check%%|*} text=${check#*|}
        local body
        body=$(curl -sS --max-time 30 "http://127.0.0.1:$port$path") || fail "$how: $path did not answer"
        case $body in *"$text"*) ;; *) fail "$how: $path does not say $text" ;; esac
    done
    for path in /lib/teas.wlg /index.wasp /web.config; do
        [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "http://127.0.0.1:$port$path")" = 404 ] || fail "$how: $path is not a 404"
    done
    echo "$how: the example's pages served."
}

# The server at the port answering a page of the site's, within 60 s: a success, so that a web server's 502, while
# wasp behind it has not yet started listening, is not taken for an answer (25 November 2026, every run on CI from
# 2e27309 failing so, nginx answering before wasp did).
ready() {
    for _ in $(seq 1 120); do
        curl -sf -o /dev/null --max-time 2 "http://127.0.0.1:$1/css/site.css" && return 0
        sleep 0.5
    done
    fail "$2 did not answer"
}

waspport=$(free)
"$wasp" fastcgi --root "$site" --port "$waspport" > "$work/wasp.log" 2>&1 &
pids+=($!)

# nginx, as the manual configures it, its files in a folder of its own.
mkdir -p "$work/nginx/logs"
port=$(free)
cat > "$work/nginx/nginx.conf" <<EOF
worker_processes 1;
error_log $work/nginx.log;
pid $work/nginx/nginx.pid;
events { worker_connections 64; }
http {
    access_log off;
    client_body_temp_path $work/nginx/body;
    fastcgi_temp_path $work/nginx/fastcgi;
    proxy_temp_path $work/nginx/proxy;
    uwsgi_temp_path $work/nginx/uwsgi;
    scgi_temp_path $work/nginx/scgi;
    upstream wasp {
        server 127.0.0.1:$waspport;
        keepalive 16;
    }
    server {
        listen 127.0.0.1:$port;
        location / {
            include /etc/nginx/fastcgi_params;
            fastcgi_keep_conn on;
            fastcgi_pass wasp;
        }
    }
}
EOF
/usr/sbin/nginx -e "$work/nginx.log" -p "$work/nginx/" -c "$work/nginx/nginx.conf" -g 'daemon off;' 2>> "$work/nginx.log" &
pids+=($!)
ready "$port" nginx
pages "$port" "nginx, to wasp fastcgi on a port"

# Apache httpd, its own configuration, the distribution's modules.
apache() {
    local name=$1 port=$2 more=$3
    mkdir -p "$work/$name" "$work/fcgid/ipc"
    cat > "$work/$name/httpd.conf" <<EOF
ServerRoot /usr/lib/apache2
LoadModule mpm_event_module /usr/lib/apache2/modules/mod_mpm_event.so
LoadModule authz_core_module /usr/lib/apache2/modules/mod_authz_core.so
$more
ServerName localhost
Listen 127.0.0.1:$port
PidFile $work/$name/httpd.pid
DefaultRuntimeDir $work/$name
Mutex file:$work/$name default
ErrorLog $work/$name.log
EOF
    /usr/sbin/apache2 -f "$work/$name/httpd.conf" -DFOREGROUND >> "$work/$name.log" 2>&1 &
    pids+=($!)
    ready "$port" "Apache httpd ($name)"
}

port=$(free)
apache proxy "$port" "LoadModule proxy_module /usr/lib/apache2/modules/mod_proxy.so
LoadModule proxy_fcgi_module /usr/lib/apache2/modules/mod_proxy_fcgi.so
ProxyPass \"/\" \"fcgi://127.0.0.1:$waspport/\""
pages "$port" "Apache httpd, mod_proxy_fcgi to wasp fastcgi on a port"

port=$(free)
apache fcgid "$port" "LoadModule fcgid_module /usr/lib/apache2/modules/mod_fcgid.so
FcgidIPCDir $work/fcgid/ipc
FcgidProcessTableFile $work/fcgid/table
DocumentRoot \"$site\"
<Directory \"$site\">
    SetHandler fcgid-script
    Options +ExecCGI
    FcgidWrapper \"$wasp fastcgi --root $site\" virtual
    Require all granted
</Directory>"
pages "$port" "Apache httpd, mod_fcgid starting wasp"

echo "Examples.wasp/example1 served on Linux through nginx, mod_proxy_fcgi and mod_fcgid, WebLingo installed from its archive."
