const { createApp } = Vue;
const tokenStorageKey = "ra3TrainerRemoteToken";

createApp({
  data() {
    return {
      busy: false,
      connected: false,
      message: "等待连接。",
      token: localStorage.getItem(tokenStorageKey),
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
      selectedUnit: null
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

      return this.status.hasAgentController ? "DLL Agent 已就绪" : "Agent 控制器尚未就绪";
    },
    selectedReinforcementPreset() {
      return this.reinforcementPresets.find(preset => preset.name === this.selectedReinforcementPresetName) || null;
    },
    selectedSecretProtocolPreset() {
      return this.secretProtocolPresets.find(preset => preset.name === this.selectedSecretProtocolPresetName) || null;
    }
  },
  async mounted() {
    if (!this.token) {
      await this.pairDevice();
    }
    if (this.token) {
      await this.refreshAll();
    }
    this.openSocket();
  },
  methods: {
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
    async refreshStatus() {
      if (!await this.ensurePaired()) {
        return;
      }

      this.busy = true;
      try {
        const response = await this.authorizedGet("/api/status");
        if (!response) return;
        this.status = await response.json();
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
        }
      } catch (error) {
        console.error("加载功能列表失败:", error);
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
        console.error("加载预设失败:", error);
      }
    },
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

      await this.post("/api/reinforcements/queue/execute", {
        entries
      });
    },
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

      await this.post("/api/secret-protocols/queue/grant", {
        entries: payloadEntries
      });
    },
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
        rank: Number(this.reinforcement.rank)
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
        upgradeId: String(this.secretProtocol.upgradeId).trim() || "0x00000000"
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
    normalizeSelectedPresetName(currentName, presets) {
      if (!presets.length) {
        return "";
      }

      return presets.some(preset => preset.name === currentName)
        ? currentName
        : presets[0].name;
    },
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
          console.error("WebSocket 消息解析失败:", e);
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
