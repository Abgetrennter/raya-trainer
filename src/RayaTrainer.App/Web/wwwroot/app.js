const { createApp } = Vue;
const tokenStorageKey = "rayaTrainerRemoteToken";
const legacyTokenStorageKey = "ra3TrainerRemoteToken";

createApp({
  data() {
    return {
      busy: false,
      connected: false,
      message: "等待连接。",
      token: localStorage.getItem(tokenStorageKey) || localStorage.getItem(legacyTokenStorageKey),
      socket: null,
      status: null,
      resources: {
        moneyAmount: 100000,
        powerValue: 100000,
        scPointValue: 15
      },
      reinforcement: {
        unitId: "0x6586A5A0",
        count: 8,
        rank: 3
      },
      secretProtocol: {
        playerTechId: "0x00000000",
        upgradeId: "0x00000000"
      },
      reinforcementQueue: [],
      secretProtocolQueue: [],
      reinforcementPresets: [],
      secretProtocolPresets: [],
      selectedReinforcementPresetName: "",
      selectedSecretProtocolPresetName: "",
      features: [],
      featuresLoading: false,
      reconnectAttempts: 0,
      gameState: null,
      selectedUnit: null,
      isOffline: !navigator.onLine,
      lastSyncedAt: null,
      reinforcementCatalog: [],
      secretProtocolCatalog: [],
      catalogLoading: false,
      picker: {
        open: false,
        type: null,
        search: "",
        faction: "全部",
        mod: "全部"
      }
    };
  },
  computed: {
    statusText() {
      if (!this.status) {
        return "未读取状态";
      }

      if (!this.status.patchesInstalled) {
        return "Patch 未安装";
      }

      return this.status.agentReady ? "DLL Agent 已就绪" : "Agent 控制器尚未就绪";
    },
    selectedReinforcementPreset() {
      return this.reinforcementPresets.find(preset => preset.name === this.selectedReinforcementPresetName) || null;
    },
    selectedSecretProtocolPreset() {
      return this.secretProtocolPresets.find(preset => preset.name === this.selectedSecretProtocolPresetName) || null;
    },
    currentCatalog() {
      if (this.picker.type === 'reinforcement') {
        return this.reinforcementCatalog.map(u => ({
          ...u,
          displayName: u.name,
          subText: `${u.codeText} · ${u.faction}`,
          selectValue: u.codeText
        }));
      } else if (this.picker.type === 'protocol') {
        return this.secretProtocolCatalog.filter(p => p.canGrant).map(p => ({
          ...p,
          displayName: p.name,
          subText: p.upgradeIdText && p.upgradeIdText !== '-' && !p.upgradeIdText.includes('无被动')
            ? `${p.playerTechIdText} · ${p.upgradeIdText.split(' ')[0]}`
            : p.playerTechIdText,
          selectValue: { playerTechId: p.playerTechId, upgradeId: p.upgradeId }
        }));
      }
      return [];
    },
    availableMods() {
      return [...new Set(this.currentCatalog.map(i => i.mod))];
    },
    filteredCatalog() {
      const search = (this.picker.search || "").trim().toLowerCase();
      return this.currentCatalog.filter(item => {
        if (this.picker.faction !== "全部" && item.faction !== this.picker.faction) return false;
        if (this.picker.mod !== "全部" && item.mod !== this.picker.mod) return false;
        if (!search) return true;
        return (item.name || "").toLowerCase().includes(search) ||
               (item.faction || "").toLowerCase().includes(search) ||
               (item.mod || "").toLowerCase().includes(search) ||
               (item.codeText || item.playerTechIdText || "").toLowerCase().includes(search) ||
               (item.subText || "").toLowerCase().includes(search);
      });
    }
  },
  async mounted() {
    // 一次性迁移旧键名（Ra3Trainer → RayaTrainer）
    const legacyToken = localStorage.getItem(legacyTokenStorageKey);
    if (legacyToken && !localStorage.getItem(tokenStorageKey)) {
      localStorage.setItem(tokenStorageKey, legacyToken);
    }
    localStorage.removeItem(legacyTokenStorageKey);

    // 加载 localStorage 缓存的最后已知状态（离线时显示）
    const cachedFeatures = localStorage.getItem("cachedFeatures");
    if (cachedFeatures) {
      try { this.features = JSON.parse(cachedFeatures); } catch (e) { /* 忽略损坏缓存 */ }
    }
    const cachedStatus = localStorage.getItem("cachedStatus");
    if (cachedStatus) {
      try { this.status = JSON.parse(cachedStatus); } catch (e) { /* 忽略损坏缓存 */ }
    }

    // 监听网络状态
    window.addEventListener("online", () => {
      this.isOffline = false;
      this.reconnectAttempts = 0;
      if (this.token && !this.socket) {
        this.openSocket();
      }
      this.refreshAll();
    });
    window.addEventListener("offline", () => { this.isOffline = true; });

    if (!this.token) {
      await this.pairDevice();
    }
    if (this.token) {
      await this.refreshAll();
    }
    // 加载缓存的目录数据（离线时即时显示）
    const cachedRein = localStorage.getItem("cachedReinforcementCatalog");
    if (cachedRein) {
      try { this.reinforcementCatalog = JSON.parse(cachedRein); } catch (e) { /* 忽略损坏缓存 */ }
    }
    const cachedProto = localStorage.getItem("cachedSecretProtocolCatalog");
    if (cachedProto) {
      try { this.secretProtocolCatalog = JSON.parse(cachedProto); } catch (e) { /* 忽略损坏缓存 */ }
    }
    // 异步刷新目录（不阻塞首屏）
    this.loadCatalogs();
    this.openSocket();
  },
  methods: {
    capabilityHint(feature) {
      const code = feature.capabilityReasonCode;
      const hints = {
        READY: { label: "就绪", action: "" },
        NO_TARGET: { label: "待连接", action: "请在电脑端检测并连接游戏进程" },
        SESSION_NOT_READY: { label: "等待中", action: "请稍候，会话正在准备" },
        PATCH_NOT_INSTALLED: { label: "待安装", action: "请在电脑端点击安装 Patch" },
        DIRECT_GAME_API_REQUIRED: { label: "需要 Agent", action: "请在电脑端确认 Agent 已注入" },
        DIRECT_GAME_API_NOT_READY: { label: "Agent 未就绪", action: "请在电脑端确认 Direct GameApi 已启用" },
        PROFILE_OR_HOOK_UNAVAILABLE: { label: "不可用", action: "该功能在当前版本/profile 下暂不支持" }
      };
      const fallback = { label: "不可用", action: feature.capabilityReason || "未知原因" };
      return hints[code] || fallback;
    },
    // ── 配对与认证 ──
    factionClass(name) {
      const map = { "苏联": "soviet", "盟军": "allied", "升阳": "imperial" };
      return map[name] || "";
    },
    async pairDevice() {
      this.busy = true;
      try {
        const response = await fetch("/api/pair", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ deviceName: navigator.userAgent || "remote browser" })
        });
        if (!response.ok) {
          const errText = await response.text().catch(() => "");
          throw new Error(errText || `HTTP ${response.status}`);
        }
        const result = await response.json();
        if (!result.token) {
          this.message = result.message ?? "设备未被允许。";
          return;
        }

        this.token = result.token;
        localStorage.setItem(tokenStorageKey, result.token);
        this.message = result.message;
      } catch (error) {
        this.message = `设备配对失败：${error.message}`;
      } finally {
        this.busy = false;
      }
    },
    // ── 状态与功能加载 ──
    async refreshStatus() {
      if (!await this.ensurePaired()) {
        return;
      }

      this.busy = true;
      try {
        const response = await this.authorizedGet("/api/status");
        if (!response) return;
        this.status = await response.json();
        localStorage.setItem("cachedStatus", JSON.stringify(this.status));
        this.lastSyncedAt = new Date().toLocaleTimeString();
        this.message = "状态已刷新。";
      } catch (error) {
        this.message = `状态读取失败：${error.message}`;
      } finally {
        this.busy = false;
      }
    },
    async setToggle(featureId, enabled) {
      await this.post(`/api/toggles/${encodeURIComponent(featureId)}`, { enabled });
    },
    async loadFeatures() {
      if (!await this.ensurePaired()) {
        return;
      }

      this.featuresLoading = true;
      try {
        const response = await this.authorizedGet("/api/features");
        if (!response) return;
        if (response.ok) {
          const data = await response.json();
          this.features = data.features || [];
          localStorage.setItem("cachedFeatures", JSON.stringify(this.features));
        }
      } catch (error) {
        this.message = `功能列表加载失败：${error.message}`;
      } finally {
        this.featuresLoading = false;
      }
    },
    async loadPresets() {
      if (!await this.ensurePaired()) {
        return;
      }

      try {
        const response = await this.authorizedGet("/api/presets");
        if (!response) return;
        if (!response.ok) {
          return;
        }

        const data = await response.json();
        this.reinforcementPresets = data.reinforcementPresets || [];
        this.secretProtocolPresets = data.secretProtocolPresets || [];
        this.selectedReinforcementPresetName = this.normalizeSelectedPresetName(
          this.selectedReinforcementPresetName,
          this.reinforcementPresets);
        this.selectedSecretProtocolPresetName = this.normalizeSelectedPresetName(
          this.selectedSecretProtocolPresetName,
          this.secretProtocolPresets);
      } catch (error) {
        this.message = `预设加载失败：${error.message}`;
      }
    },
    async loadCatalogs() {
      if (!await this.ensurePaired()) {
        return;
      }

      this.catalogLoading = true;
      try {
        const [reinRes, protoRes] = await Promise.all([
          this.authorizedGet("/api/reinforcements/catalog"),
          this.authorizedGet("/api/secret-protocols/catalog")
        ]);

        if (reinRes && reinRes.ok) {
          const data = await reinRes.json();
          this.reinforcementCatalog = data.entries || [];
          localStorage.setItem("cachedReinforcementCatalog", JSON.stringify(this.reinforcementCatalog));
        }

        if (protoRes && protoRes.ok) {
          const data = await protoRes.json();
          this.secretProtocolCatalog = data.entries || [];
          localStorage.setItem("cachedSecretProtocolCatalog", JSON.stringify(this.secretProtocolCatalog));
        }
      } catch (error) {
        this.message = `目录加载失败：${error.message}`;
      } finally {
        this.catalogLoading = false;
      }
    },
    // ── 功能操作 ──
    async toggleFeature(featureId, enabled) {
      await this.post(`/api/toggles/${encodeURIComponent(featureId)}`, { enabled });
      await this.loadFeatures();
    },
    async refreshAll() {
      await this.refreshStatus();
      await this.loadFeatures();
      await this.loadPresets();
    },
    async executeAction(featureId, params = {}) {
      await this.post(`/api/actions/${encodeURIComponent(featureId)}`, params);
    },
    async writeResources() {
      await this.post("/api/resources", this.resources);
    },
    async readSelectedUnit() {
      if (!await this.ensurePaired()) {
        return;
      }

      try {
        const response = await this.authorizedGet("/api/selected-unit");
        if (!response) return;
        if (response.ok) {
          const data = await response.json();
          if (data.unitCodeHex) {
            this.reinforcement.unitId = data.unitCodeHex;
            this.message = `已读取选中单位：${data.unitCodeHex}`;
          } else {
            this.message = "未选中有效单位，请在游戏中先选中一个单位。";
          }
        }
      } catch (error) {
        this.message = `读取选中单位失败：${error.message}。请确认游戏正在运行且已选中单位。`;
      }
    },
    // ── 增援 ──
    async executeReinforcement() {
      const unitId = this.parseRequiredNumber(this.reinforcement.unitId, "单位 ID");
      if (unitId === null) return;

      await this.post("/api/reinforcements/execute", {
        unitId,
        count: Number(this.reinforcement.count),
        rank: Number(this.reinforcement.rank)
      });
    },
    async executeReinforcementQueue() {
      const entries = [];
      for (const [index, item] of this.reinforcementQueue.entries()) {
        const unitId = this.parseRequiredNumber(item.unitId, `增援队列第 ${index + 1} 项单位 ID`);
        if (unitId === null) return;

        entries.push({
          unitId,
          count: Number(item.count),
          rank: Number(item.rank)
        });
      }

      // 重置所有项为 Pending
      this.reinforcementQueue.forEach(item => { item.status = "Pending"; item.statusMessage = ""; });

      if (!await this.ensurePaired()) {
        return;
      }

      this.busy = true;
      try {
        const response = await fetch("/api/reinforcements/queue/execute", {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${this.token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify({ entries })
        });
        if (response.status === 401) {
          await this.handleUnauthorized();
          return;
        }

        if (response.ok) {
          const result = await response.json();
          if (result.items) {
            result.items.forEach(item => {
              if (this.reinforcementQueue[item.index]) {
                this.reinforcementQueue[item.index].status = item.status;
                this.reinforcementQueue[item.index].statusMessage = item.message || "";
              }
            });
          }
          this.message = result.message || "队列执行完成。";
        } else {
          const errText = await response.text().catch(() => "");
          throw new Error(errText || `HTTP ${response.status}`);
        }
        await this.refreshStatus();
      } catch (error) {
        this.message = `命令发送失败：${error.message}`;
      } finally {
        this.busy = false;
      }
    },
    // ── 秘密协议 ──
    async grantSecretProtocol() {
      const playerTechId = this.parseRequiredNumber(this.secretProtocol.playerTechId, "PlayerTech");
      const upgradeId = this.parseRequiredNumber(this.secretProtocol.upgradeId, "Upgrade");
      if (playerTechId === null || upgradeId === null) return;

      await this.post("/api/secret-protocols/grant", {
        playerTechId,
        upgradeId
      });
    },
    async grantSecretProtocolQueue(entries = this.secretProtocolQueue) {
      const payloadEntries = [];
      for (const [index, item] of entries.entries()) {
        const playerTechId = this.parseRequiredNumber(item.playerTechId, `协议队列第 ${index + 1} 项 PlayerTech`);
        const upgradeId = this.parseRequiredNumber(item.upgradeId, `协议队列第 ${index + 1} 项 Upgrade`);
        if (playerTechId === null || upgradeId === null) return;

        payloadEntries.push({
          playerTechId,
          upgradeId
        });
      }

      // 重置所有项为 Pending
      entries.forEach(item => { item.status = "Pending"; item.statusMessage = ""; });

      if (!await this.ensurePaired()) {
        return;
      }

      this.busy = true;
      try {
        const response = await fetch("/api/secret-protocols/queue/grant", {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${this.token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify({ entries: payloadEntries })
        });
        if (response.status === 401) {
          await this.handleUnauthorized();
          return;
        }

        if (response.ok) {
          const result = await response.json();
          if (result.items) {
            result.items.forEach(item => {
              if (this.secretProtocolQueue[item.index]) {
                this.secretProtocolQueue[item.index].status = item.status;
                this.secretProtocolQueue[item.index].statusMessage = item.message || "";
              }
            });
          }
          this.message = result.message || "队列执行完成。";
        } else {
          const errText = await response.text().catch(() => "");
          throw new Error(errText || `HTTP ${response.status}`);
        }
        await this.refreshStatus();
      } catch (error) {
        this.message = `命令发送失败：${error.message}`;
      } finally {
        this.busy = false;
      }
    },
    // ── 预设管理（增援/协议队列填充） ──
    applyReinforcementPreset(preset) {
      const entries = this.toReinforcementQueueEntries(preset);
      if (!entries.length) {
        this.message = "选中预设没有条目。";
        return;
      }

      this.reinforcementQueue = entries;
      this.applyReinforcementEntry(entries[0]);
      this.message = `已应用增援预设：${preset.name}`;
    },
    applySelectedReinforcementPreset() {
      if (!this.selectedReinforcementPreset) {
        this.message = "请选择增援预设。";
        return;
      }

      this.applyReinforcementPreset(this.selectedReinforcementPreset);
    },
    appendSelectedReinforcementPreset() {
      if (!this.selectedReinforcementPreset) {
        this.message = "请选择增援预设。";
        return;
      }

      this.appendReinforcementPreset(this.selectedReinforcementPreset);
    },
    appendReinforcementPreset(preset) {
      const entries = this.toReinforcementQueueEntries(preset);
      if (!entries.length) {
        this.message = "选中预设没有条目。";
        return;
      }

      this.reinforcementQueue.push(...entries);
      this.message = `已追加增援预设：${preset.name}`;
    },
    applyReinforcementEntry(entry) {
      this.reinforcement.unitId = entry.unitId;
      this.reinforcement.count = entry.count;
      this.reinforcement.rank = entry.rank;
    },
    toReinforcementQueueEntries(preset) {
      return (preset.entries || []).map(entry => ({
        name: entry.name,
        unitId: entry.unitIdText,
        count: entry.count,
        rank: entry.rank
      }));
    },
    addCurrentReinforcementToQueue() {
      const unitId = String(this.reinforcement.unitId).trim();
      this.reinforcementQueue.push({
        name: unitId || "当前增援",
        unitId: unitId || "0x00000000",
        count: Number(this.reinforcement.count),
        rank: Number(this.reinforcement.rank),
        status: "Pending",
        statusMessage: ""
      });
      this.message = "已加入当前增援到队列。";
    },
    clearReinforcementQueue() {
      this.reinforcementQueue = [];
      this.message = "增援队列已清空。";
    },
    applySelectedSecretProtocolPreset() {
      if (!this.selectedSecretProtocolPreset) {
        this.message = "请选择秘密协议预设。";
        return;
      }

      this.applySecretProtocolPreset(this.selectedSecretProtocolPreset);
    },
    applySecretProtocolPreset(preset) {
      const entries = this.toSecretProtocolQueueEntries(preset);
      if (!entries.length) {
        this.message = "选中预设没有条目。";
        return;
      }

      this.secretProtocolQueue = entries;
      if (this.secretProtocolQueue.length) {
        this.applySecretProtocolEntry(this.secretProtocolQueue[0]);
      }
      this.message = `已应用秘密协议预设：${preset.name}`;
    },
    appendSelectedSecretProtocolPreset() {
      if (!this.selectedSecretProtocolPreset) {
        this.message = "请选择秘密协议预设。";
        return;
      }

      this.appendSecretProtocolPreset(this.selectedSecretProtocolPreset);
    },
    appendSecretProtocolPreset(preset) {
      const entries = this.toSecretProtocolQueueEntries(preset);
      if (!entries.length) {
        this.message = "选中预设没有条目。";
        return;
      }

      this.secretProtocolQueue.push(...entries);
      this.message = `已追加秘密协议预设：${preset.name}`;
    },
    addCurrentSecretProtocolToQueue() {
      this.secretProtocolQueue.push({
        name: String(this.secretProtocol.upgradeId).trim() || "当前协议",
        faction: "当前",
        playerTechId: String(this.secretProtocol.playerTechId).trim() || "0x00000000",
        upgradeId: String(this.secretProtocol.upgradeId).trim() || "0x00000000",
        status: "Pending",
        statusMessage: ""
      });
      this.message = "已加入当前秘密协议到授予列表。";
    },
    applySecretProtocolEntry(entry) {
      this.secretProtocol.playerTechId = entry.playerTechId;
      this.secretProtocol.upgradeId = entry.upgradeId;
    },
    toSecretProtocolQueueEntries(preset) {
      return (preset.entries || []).map(entry => ({
        name: entry.name,
        faction: entry.faction,
        playerTechId: entry.playerTechIdText,
        upgradeId: entry.upgradeIdText
      }));
    },
    clearSecretProtocolQueue() {
      this.secretProtocolQueue = [];
      this.message = "秘密协议添加列表已清空。";
    },
    openPicker(type) {
      this.picker.open = true;
      this.picker.type = type;
      this.picker.search = "";
      this.picker.faction = "全部";
      this.picker.mod = "全部";
    },
    closePicker() {
      this.picker.open = false;
    },
    selectItem(item) {
      if (this.picker.type === 'reinforcement') {
        this.reinforcement.unitId = item.selectValue;
      } else if (this.picker.type === 'protocol') {
        const sv = item.selectValue;
        // catalog DTO 已暴露数字 playerTechId/upgradeId (uint)，直接格式化为 0xXXXXXXXX。
        // parseNumber/parseRequiredNumber 要求 0x 前缀十六进制；0 表示该项不适用，后端接受 0。
        this.secretProtocol.playerTechId = "0x" + (sv.playerTechId >>> 0).toString(16).toUpperCase().padStart(8, "0");
        this.secretProtocol.upgradeId = "0x" + (sv.upgradeId >>> 0).toString(16).toUpperCase().padStart(8, "0");
      }
      this.closePicker();
    },
    dotClass(faction) {
      if (faction.includes("盟军")) return "dot-allied";
      if (faction.includes("苏联")) return "dot-soviet";
      if (faction.includes("升阳")) return "dot-imperial";
      return "dot-other";
    },
    // ── 工具方法 ──
    queueStatusText(status) {
      const map = {
        Pending: "等待",
        Executing: "执行中",
        Executed: "已执行",
        Skipped: "已跳过",
        TimedOut: "超时",
        Failed: "失败",
        AbortedDueToPause: "已中止"
      };
      return map[status] || status;
    },
    queueStatusColor(status) {
      const map = {
        Pending: "#888",
        Executing: "#4a9eff",
        Executed: "#4ade80",
        Skipped: "#a0a0a0",
        TimedOut: "#f39c12",
        Failed: "#f87171",
        AbortedDueToPause: "#f87171"
      };
      return map[status] || "#888";
    },
    normalizeSelectedPresetName(currentName, presets) {
      if (!presets.length) {
        return "";
      }

      return presets.some(preset => preset.name === currentName)
        ? currentName
        : presets[0].name;
    },
    // ── 网络层（HTTP / WebSocket） ──
    async ensurePaired() {
      if (!this.token) {
        await this.pairDevice();
      }

      return !!this.token;
    },
    async authorizedGet(url) {
      const response = await fetch(url, {
        headers: {
          "Authorization": `Bearer ${this.token}`
        }
      });

      if (response.status === 401) {
        await this.handleUnauthorized();
        return null;
      }

      return response;
    },
    async post(url, payload) {
      if (!await this.ensurePaired()) {
        return;
      }

      this.busy = true;
      try {
        const response = await fetch(url, {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${this.token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify(payload)
        });
        if (response.status === 401) {
          await this.handleUnauthorized();
          return;
        }

        if (!response.ok) {
          const errText = await response.text().catch(() => "");
          throw new Error(errText || `HTTP ${response.status}`);
        }

        const result = await response.json();
        this.message = result.message ?? "命令已执行。";
        await this.refreshStatus();
      } catch (error) {
        this.message = `命令发送失败：${error.message}`;
      } finally {
        this.busy = false;
      }
    },
    async handleUnauthorized() {
      localStorage.removeItem(tokenStorageKey);
      this.token = null;
      this.message = "配对已失效，请重新允许设备。";
      await this.pairDevice();
    },
    openSocket() {
      if (!this.token) return;
      if (this.socket) {
        this.socket.close();
        this.socket = null;
      }

      const scheme = location.protocol === "https:" ? "wss" : "ws";
      const socket = new WebSocket(`${scheme}://${location.host}/api/ws`);
      this.socket = socket;

      socket.onopen = () => {
        socket.send(JSON.stringify({ token: this.token }));
        this.connected = true;
        this.reconnectAttempts = 0;
      };

      socket.onmessage = event => {
        try {
          const update = JSON.parse(event.data);
          this.handleWebSocketMessage(update);
        } catch (e) {
          this.message = `WebSocket 消息解析失败：${e.message}`;
        }
      };

      socket.onclose = async event => {
        this.socket = null;
        this.connected = false;
        if (event.code === 1008) {
          await this.handleUnauthorized();
          this.openSocket();
          return;
        }

        if (this.token) {
          const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000);
          this.reconnectAttempts++;
          if (this.reconnectAttempts > 1) {
            this.message = `连接断开，${Math.round(delay / 1000)} 秒后重连（第 ${this.reconnectAttempts} 次）…`;
          }
          setTimeout(() => this.openSocket(), delay);
        }
      };

      socket.onerror = () => {
        this.connected = false;
        socket.close();
      };
    },
    handleWebSocketMessage(update) {
      if (update.sessionStatus) {
        this.status = update.sessionStatus;
      }
      if (update.gameState) {
        this.gameState = update.gameState;
      }
      if (update.selectedUnit) {
        this.selectedUnit = update.selectedUnit;
      }
      if (update.features) {
        this.features = update.features.features || [];
      }
      if (update.type !== "heartbeat") {
        this.message = update.message ?? this.message;
      }
    },
    // ── 输入解析 ──
    parseRequiredNumber(value, label) {
      const parsed = this.parseNumber(value);
      if (Number.isNaN(parsed)) {
        this.message = `${label} 不是有效数字。`;
        return null;
      }

      return parsed;
    },
    parseNumber(value) {
      if (typeof value === "number") {
        return value;
      }

      const text = String(value).trim();
      return text.toLowerCase().startsWith("0x")
        ? Number.parseInt(text.slice(2), 16)
        : Number.parseInt(text, 10);
    }
  }
}).mount("#app");
