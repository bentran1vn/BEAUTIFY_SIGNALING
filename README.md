# Janus Setup

### **Install Dependencies**

```python
sudo apt update
sudo apt install -y build-essential git cmake libmicrohttpd-dev libjansson-dev \
  libssl-dev libsrtp2-dev libsofia-sip-ua-dev libglib2.0-dev libopus-dev \
  libogg-dev libcurl4-openssl-dev liblua5.3-dev libconfig-dev pkg-config \
  gengetopt libtool automake libwebsockets-dev
```

**For Video Support**

```
sudo apt install -y libavutil-dev libavcodec-dev libavformat-dev \
  libswscale-dev libavfilter-dev libnice-dev
```

**Clone Janus GitHub Repository**

```
git clone https://github.com/meetecho/janus-gateway.git
cd janus-gateway
```

### **Build and Install Janus**

```
sh autogen.sh
./configure --enable-websockets
make
sudo make install
sudo make configs
```

**Enable WebSockets in the Janus config**

```markdown
sudo nano /usr/local/etc/janus/janus.transport.websockets.cfg
```

```markdown
[general]
enabled = true
ws = true
ws_port = 8188
```

**Create a Systemd Service File**

```python
sudo nano /etc/systemd/system/janus.service
```

```
[Unit]Description=Janus WebRTC Gateway
After=network.target

[Service]Type=simple
ExecStart=/usr/local/bin/janus -F /usr/local/etc/janus
Restart=on-failure
User=root
LimitNOFILE=4096

[Install]WantedBy=multi-user.target
```

**Reload Systemd and Enable the Service**

```
sudo systemctl daemon-reload
sudo systemctl enable janus
sudo systemctl start janus
sudo systemctl status janus
journalctl -u janus -f
```

> "Try installing libsrtp2 manually, not from repository.”
> 

**Remove the repo version if installed (optional but recommended)**

```markdown
sudo apt remove --purge libsrtp2-dev libsrtp2-1
```

**Download and build libsrtp2 from source**

```markdown
cd /usr/local/src
sudo git clone https://github.com/cisco/libsrtp.git
cd libsrtp
sudo git checkout v2.4.2   # You can check for the latest release on GitHub if needed
sudo ./configure --prefix=/usr/local --enable-openssl
sudo make
sudo make install
```

**Update library cache (so the system finds the new libraries)**

```markdown
sudo ldconfig
```

**Rebuild Janus:**

```markdown
cd ~/janus-gateway
make clean
./configure --enable-websockets
make
sudo make install
sudo make configs
```

**Reload Systemd**

```markdown
sudo systemctl restart janus
```
