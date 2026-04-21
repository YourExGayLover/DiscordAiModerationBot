let selectedGuildId = null;
let selectedGuildName = null;
let selectedChannelId = null;
let selectedChannelName = null;
let allGuilds = [];
let allChannels = [];
let loadedMessages = [];
let nextBeforeMessageId = null;
let hasMoreMessages = false;
let refreshTimer = null;
let isLoadingMessages = false;
let isLoadingOlderMessages = false;
let autoRefreshEnabled = true;

async function getJson(url) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`Request failed: ${response.status}`);
    }
    return response.json();
}

function el(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
}

function initials(name) {
    return (name || '?')
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map(part => part[0]?.toUpperCase() ?? '')
        .join('') || '?';
}

function formatBytes(size) {
    if (!size || size < 1) return 'Unknown size';
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = size;
    let unitIndex = 0;

    while (value >= 1024 && unitIndex < units.length - 1) {
        value /= 1024;
        unitIndex += 1;
    }

    return `${value.toFixed(value >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
}

function isImageFile(fileName) {
    return /\.(png|jpe?g|gif|webp|bmp|svg)$/i.test(fileName || '');
}

function setStatus(text, isError = false) {
    const status = document.getElementById('status');
    status.textContent = text;
    status.style.color = isError ? 'var(--danger)' : 'var(--muted)';
}

function setInspector() {
    document.getElementById('inspectorGuildName').textContent = selectedGuildName ?? '-';
    document.getElementById('inspectorChannelName').textContent = selectedChannelName ? `# ${selectedChannelName}` : '-';
    document.getElementById('inspectorLoadedCount').textContent = `${loadedMessages.length} message${loadedMessages.length === 1 ? '' : 's'}`;
}

function setMessageMeta() {
    const meta = document.getElementById('messageMeta');

    if (!selectedChannelId) {
        meta.textContent = 'Choose a channel to load the newest page.';
        return;
    }

    if (!loadedMessages.length) {
        meta.textContent = 'No messages loaded yet.';
        return;
    }

    meta.textContent = `${loadedMessages.length} loaded • newest at bottom${hasMoreMessages ? ' • older pages available' : ''}`;
}

function updateLoadOlderButton() {
    const button = document.getElementById('loadOlderButton');
    if (!button) return;

    button.disabled = !selectedChannelId || !hasMoreMessages || !nextBeforeMessageId;
}

function getMessageScrollHost() {
    return document.getElementById('messages');
}

function isNearBottom(container, threshold = 48) {
    return container.scrollHeight - container.scrollTop - container.clientHeight <= threshold;
}

function scrollMessagesToBottom() {
    const container = getMessageScrollHost();
    container.scrollTop = container.scrollHeight;
}

function ensureAutoRefreshToggle() {
    const refreshButton = document.getElementById('refreshButton');
    if (!refreshButton) return;

    if (document.getElementById('autoRefreshToggle')) {
        return;
    }

    const label = document.createElement('label');
    label.id = 'autoRefreshToggleLabel';
    label.style.display = 'inline-flex';
    label.style.alignItems = 'center';
    label.style.gap = '6px';
    label.style.fontSize = '13px';
    label.style.color = 'var(--text)';
    label.style.userSelect = 'none';
    label.style.marginLeft = '10px';

    const input = document.createElement('input');
    input.type = 'checkbox';
    input.id = 'autoRefreshToggle';
    input.checked = true;
    input.style.margin = '0';

    input.addEventListener('change', () => {
        autoRefreshEnabled = input.checked;
    });

    label.append(input);
    label.append(document.createTextNode('Auto refresh'));

    refreshButton.insertAdjacentElement('afterend', label);
    autoRefreshEnabled = input.checked;
}

async function loadStatus() {
    const status = await getJson('/api/status');
    setStatus(`User: ${status.currentUser ?? 'n/a'} • ${status.connectionState} • ${status.guildCount} guilds`);
}

function renderGuildRail() {
    const host = document.getElementById('guildRail');
    host.innerHTML = '';

    for (const guild of allGuilds) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `guild-pill${guild.id === selectedGuildId ? ' active' : ''}`;
        button.title = guild.name;

        if (guild.iconUrl) {
            const image = document.createElement('img');
            image.src = guild.iconUrl;
            image.alt = guild.name;
            button.append(image);
        } else {
            button.textContent = initials(guild.name);
        }

        button.onclick = async () => {
            selectedGuildId = guild.id;
            selectedGuildName = guild.name;
            selectedChannelId = null;
            selectedChannelName = null;
            loadedMessages = [];
            nextBeforeMessageId = null;
            hasMoreMessages = false;

            renderGuildRail();
            renderMessages([]);
            renderChannels();
            await loadChannels();
        };

        host.append(button);
    }
}

async function loadGuilds() {
    allGuilds = await getJson('/api/guilds');

    if (!selectedGuildId && allGuilds.length > 0) {
        selectedGuildId = allGuilds[0].id;
        selectedGuildName = allGuilds[0].name;
    }

    if (selectedGuildId && !allGuilds.some(g => g.id === selectedGuildId)) {
        selectedGuildId = allGuilds[0]?.id ?? null;
        selectedGuildName = allGuilds[0]?.name ?? null;
    }

    if (selectedGuildId && !selectedGuildName) {
        selectedGuildName = allGuilds.find(g => g.id === selectedGuildId)?.name ?? null;
    }

    renderGuildRail();
    document.getElementById('guildName').textContent = selectedGuildName ?? 'Select a server';
}

function groupChannels(channels) {
    const grouped = new Map();

    for (const channel of channels) {
        const key = channel.categoryName || 'Uncategorized';
        if (!grouped.has(key)) {
            grouped.set(key, []);
        }

        grouped.get(key).push(channel);
    }

    return [...grouped.entries()];
}

function renderChannels() {
    const host = document.getElementById('channels');
    host.innerHTML = '';

    const searchTerm = document.getElementById('channelSearch').value.trim().toLowerCase();
    const guildChannels = allChannels
        .filter(channel => channel.guildId === selectedGuildId)
        .filter(channel =>
            !searchTerm ||
            channel.name.toLowerCase().includes(searchTerm) ||
            (channel.categoryName || '').toLowerCase().includes(searchTerm));

    if (!guildChannels.length) {
        const empty = el('div', 'empty-copy', searchTerm ? 'No channels matched your search.' : 'No channels available.');
        host.append(empty);
        return;
    }

    for (const [categoryName, channels] of groupChannels(guildChannels)) {
        const section = el('section', 'channel-group');
        section.append(el('div', 'channel-group-title', categoryName));

        for (const channel of channels) {
            const button = el('button', `channel-item${channel.id === selectedChannelId ? ' active' : ''}`);
            button.type = 'button';

            const left = el('div', 'channel-left');
            left.append(el('span', 'channel-hash', '#'));

            const nameWrap = el('div', 'channel-name-wrap');
            nameWrap.append(el('div', 'channel-name', channel.name));
            nameWrap.append(el('div', 'channel-meta', `${channel.recentMessageCount} live cached`));
            left.append(nameWrap);
            button.append(left);

            const badge = el('span', 'channel-badge', `${channel.recentMessageCount}`);
            button.append(badge);

            button.onclick = async () => {
                selectedChannelId = channel.id;
                selectedChannelName = channel.name;
                loadedMessages = [];
                nextBeforeMessageId = null;
                hasMoreMessages = false;

                document.getElementById('currentChannel').textContent = `# ${channel.name}`;
                renderChannels();
                await loadMessages(true, { scrollMode: 'bottom' });
            };

            section.append(button);
        }

        host.append(section);
    }

    setInspector();
}

async function loadChannels() {
    allChannels = await getJson(`/api/channels${selectedGuildId ? `?guildId=${encodeURIComponent(selectedGuildId)}` : ''}`);
    renderChannels();
}

function createAvatar(message) {
    if (message.authorAvatarUrl) {
        const image = document.createElement('img');
        image.className = 'avatar';
        image.src = message.authorAvatarUrl;
        image.alt = message.authorName;
        return image;
    }

    return el('div', 'avatar-fallback', initials(message.authorName));
}

function createAttachment(attachment) {
    const card = document.createElement('a');
    card.className = 'attachment-card';
    card.href = attachment.url;
    card.target = '_blank';
    card.rel = 'noreferrer noopener';

    if (isImageFile(attachment.fileName)) {
        const image = document.createElement('img');
        image.className = 'attachment-image';
        image.src = attachment.url;
        image.alt = attachment.fileName;
        card.append(image);
    }

    const meta = el('div', 'attachment-meta');
    meta.append(el('div', 'attachment-name', attachment.fileName));
    meta.append(el('div', 'attachment-size', formatBytes(attachment.size)));
    card.append(meta);

    return card;
}

function renderMessages(messages) {
    const host = getMessageScrollHost();
    host.innerHTML = '';

    if (!selectedChannelId) {
        const state = el('div', 'empty-state');
        state.append(el('div', 'empty-title', 'Open a channel'));
        state.append(el('div', 'empty-copy', 'Choose a server on the left, then pick a text channel to load the newest messages at the bottom.'));
        host.append(state);
        setMessageMeta();
        setInspector();
        updateLoadOlderButton();
        return;
    }

    if (!messages.length) {
        const state = el('div', 'empty-state');
        state.append(el('div', 'empty-title', 'No messages loaded yet'));
        state.append(el('div', 'empty-copy', 'Try Refresh, or load a different channel.'));
        host.append(state);
        setMessageMeta();
        setInspector();
        updateLoadOlderButton();
        return;
    }

    const displayMessages = [...messages].reverse();

    for (const message of displayMessages) {
        const card = el('article', 'message-card');
        card.append(createAvatar(message));

        const main = el('div', 'message-main');
        const topline = el('div', 'message-topline');
        topline.append(el('span', 'message-author', message.authorName));

        if (message.isBot) {
            topline.append(el('span', 'bot-tag', 'BOT'));
        }

        topline.append(el('span', 'message-time', new Date(message.timestamp).toLocaleString()));
        main.append(topline);
        main.append(el('div', 'message-content', message.content || '(no text content)'));

        if (message.attachments?.length) {
            const grid = el('div', 'attachments-grid');
            for (const attachment of message.attachments) {
                grid.append(createAttachment(attachment));
            }

            main.append(grid);
        }

        card.append(main);
        host.append(card);
    }

    setMessageMeta();
    setInspector();
    updateLoadOlderButton();
}

function mergeNewestFirst(existing, incoming) {
    const map = new Map();

    for (const message of [...existing, ...incoming]) {
        map.set(message.id, message);
    }

    return [...map.values()].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
}

async function loadMessages(reset = false, options = {}) {
    if (!selectedChannelId) {
        loadedMessages = [];
        renderMessages([]);
        return;
    }

    if (reset) {
        if (isLoadingMessages) return;
        isLoadingMessages = true;
    } else {
        if (isLoadingOlderMessages || isLoadingMessages || !nextBeforeMessageId || !hasMoreMessages) return;
        isLoadingOlderMessages = true;
    }

    const { scrollMode = 'preserve' } = options;
    const container = getMessageScrollHost();
    const wasNearBottom = isNearBottom(container);
    const previousScrollHeight = container.scrollHeight;
    const previousScrollTop = container.scrollTop;

    try {
        const beforePart = !reset && nextBeforeMessageId
            ? `?beforeMessageId=${encodeURIComponent(nextBeforeMessageId)}`
            : '';

        const page = await getJson(`/api/channels/${encodeURIComponent(selectedChannelId)}/messages${beforePart}`);

        if (reset) {
            loadedMessages = page.items;
        } else {
            loadedMessages = mergeNewestFirst(loadedMessages, page.items);
        }

        hasMoreMessages = page.hasMore;
        nextBeforeMessageId = page.nextBeforeMessageId;
        renderMessages(loadedMessages);

        if (reset) {
            if (scrollMode === 'bottom' || wasNearBottom) {
                scrollMessagesToBottom();
            } else {
                container.scrollTop = previousScrollTop;
            }
        } else {
            const newScrollHeight = container.scrollHeight;
            container.scrollTop = previousScrollTop + (newScrollHeight - previousScrollHeight);
        }
    } finally {
        if (reset) {
            isLoadingMessages = false;
        } else {
            isLoadingOlderMessages = false;
        }
    }
}

async function refreshLatestMessages(options = {}) {
    if (!selectedChannelId || isLoadingMessages || isLoadingOlderMessages) {
        return;
    }

    const container = getMessageScrollHost();

    // Prevent the periodic refresh from snapping the user back to the newest page
    // while they are reading older messages above the bottom.
    if (!isNearBottom(container)) {
        return;
    }

    isLoadingMessages = true;
    const { scrollMode = 'preserve' } = options;
    const wasNearBottom = isNearBottom(container);
    const previousScrollTop = container.scrollTop;
    const previousScrollHeight = container.scrollHeight;

    try {
        const page = await getJson(`/api/channels/${encodeURIComponent(selectedChannelId)}/messages`);
        loadedMessages = mergeNewestFirst(loadedMessages, page.items);

        if (!nextBeforeMessageId || loadedMessages.length <= page.items.length) {
            nextBeforeMessageId = page.nextBeforeMessageId;
            hasMoreMessages = page.hasMore;
        }

        renderMessages(loadedMessages);

        if (scrollMode === 'bottom' || wasNearBottom) {
            scrollMessagesToBottom();
        } else {
            const newScrollHeight = container.scrollHeight;
            container.scrollTop = previousScrollTop + (newScrollHeight - previousScrollHeight);
        }
    } finally {
        isLoadingMessages = false;
    }
}

async function refreshAll() {
    await loadStatus();
    await loadGuilds();
    await loadChannels();

    if (selectedChannelId) {
        await loadMessages(true, { scrollMode: 'preserve' });
    } else {
        renderMessages([]);
    }
}

function startAutoRefresh() {
    if (refreshTimer) {
        clearInterval(refreshTimer);
    }

    refreshTimer = setInterval(() => {
        if (autoRefreshEnabled && selectedChannelId) {
            refreshLatestMessages({ scrollMode: 'preserve' }).catch(() => {});
        }

        loadStatus().catch(() => {});
    }, 5000);
}

document.getElementById('refreshAllButton').addEventListener('click', () => {
    refreshAll().catch(error => setStatus(error.message, true));
});

document.getElementById('refreshButton').addEventListener('click', () => {
    loadMessages(true, { scrollMode: 'bottom' }).catch(error => setStatus(error.message, true));
});

const messageScrollHost = getMessageScrollHost();
messageScrollHost.addEventListener('scroll', () => {
    if (!selectedChannelId || !hasMoreMessages || !nextBeforeMessageId || isLoadingOlderMessages || isLoadingMessages) {
        return;
    }

    if (messageScrollHost.scrollTop <= 80) {
        loadMessages(false).catch(error => setStatus(error.message, true));
    }
});

document.getElementById('channelSearch').addEventListener('input', () => {
    renderChannels();
});

ensureAutoRefreshToggle();

refreshAll()
    .then(startAutoRefresh)
    .catch(error => setStatus(error.message, true));
