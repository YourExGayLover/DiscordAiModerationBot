let selectedGuildId = null;
let selectedChannelId = null;
let loadedMessages = [];
let nextBeforeMessageId = null;
let hasMoreMessages = false;

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

function setMessageMeta() {
    const meta = document.getElementById('messageMeta');
    if (!selectedChannelId) {
        meta.textContent = '';
        return;
    }

    if (!loadedMessages.length) {
        meta.textContent = 'No messages loaded yet.';
        return;
    }

    meta.textContent = `${loadedMessages.length} loaded • newest first${hasMoreMessages ? ' • more available' : ''}`;
}

function updateLoadOlderButton() {
    const button = document.getElementById('loadOlderButton');
    button.disabled = !selectedChannelId || !hasMoreMessages || !nextBeforeMessageId;
}

async function loadStatus() {
    const status = await getJson('/api/status');
    document.getElementById('status').textContent =
        `User: ${status.currentUser ?? 'n/a'} | Logged In: ${status.isLoggedIn} | Connection: ${status.connectionState} | Guilds: ${status.guildCount}`;
}

async function loadGuilds() {
    const guilds = await getJson('/api/guilds');
    const host = document.getElementById('guilds');
    host.innerHTML = '';

    for (const guild of guilds) {
        const button = el('button', 'list-item' + (guild.id === selectedGuildId ? ' active' : ''));
        button.append(el('div', null, guild.name));
        button.append(el('span', 'secondary', `${guild.textChannelCount} text channels`));
        button.onclick = async () => {
            selectedGuildId = guild.id;
            selectedChannelId = null;
            loadedMessages = [];
            nextBeforeMessageId = null;
            hasMoreMessages = false;
            await loadGuilds();
            await loadChannels();
            renderMessages([]);
            document.getElementById('currentChannel').textContent = 'Select a channel';
            setMessageMeta();
            updateLoadOlderButton();
        };
        host.append(button);
    }

    if (!selectedGuildId && guilds.length > 0) {
        selectedGuildId = guilds[0].id;
        await loadGuilds();
        await loadChannels();
    }
}

async function loadChannels() {
    const channels = await getJson(`/api/channels${selectedGuildId ? `?guildId=${encodeURIComponent(selectedGuildId)}` : ''}`);
    const host = document.getElementById('channels');
    host.innerHTML = '';

    for (const channel of channels) {
        const button = el('button', 'list-item' + (channel.id === selectedChannelId ? ' active' : ''));
        button.append(el('div', null, `# ${channel.name}`));
        button.append(el('span', 'secondary', `${channel.recentMessageCount} live cached messages`));
        button.onclick = async () => {
            selectedChannelId = channel.id;
            loadedMessages = [];
            nextBeforeMessageId = null;
            hasMoreMessages = false;
            document.getElementById('currentChannel').textContent = `# ${channel.name}`;
            await loadChannels();
            await loadMessages(true);
        };
        host.append(button);
    }
}

function renderMessages(messages) {
    const host = document.getElementById('messages');
    host.innerHTML = '';

    if (!messages.length) {
        host.append(el('div', 'status', 'No messages loaded for this channel yet.'));
        return;
    }

    for (const message of messages) {
        const wrapper = el('article', 'message');
        const header = el('div', 'message-header');
        header.append(el('span', 'author', message.authorName + (message.isBot ? ' [bot]' : '')));
        header.append(el('span', 'timestamp', new Date(message.timestamp).toLocaleString()));
        wrapper.append(header);
        wrapper.append(el('div', 'content', message.content || '(no text content)'));

        if (message.attachments?.length) {
            const attachments = el('div', 'attachments');
            for (const attachment of message.attachments) {
                const link = document.createElement('a');
                link.href = attachment.url;
                link.target = '_blank';
                link.rel = 'noreferrer noopener';
                link.textContent = attachment.fileName;
                attachments.append(link);
                attachments.append(document.createElement('br'));
            }
            wrapper.append(attachments);
        }

        host.append(wrapper);
    }
}

function mergeNewestFirst(existing, incoming) {
    const map = new Map();

    for (const message of [...existing, ...incoming]) {
        map.set(message.id, message);
    }

    return [...map.values()].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
}

async function loadMessages(reset = false) {
    if (!selectedChannelId) {
        loadedMessages = [];
        renderMessages([]);
        setMessageMeta();
        updateLoadOlderButton();
        return;
    }

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
    setMessageMeta();
    updateLoadOlderButton();
}

async function refreshAll() {
    await loadStatus();
    await loadGuilds();
    if (selectedChannelId) {
        await loadMessages(true);
    }
}

document.getElementById('refreshButton').addEventListener('click', () => {
    loadMessages(true).catch(error => {
        document.getElementById('status').textContent = error.message;
    });
});

document.getElementById('loadOlderButton').addEventListener('click', () => {
    loadMessages(false).catch(error => {
        document.getElementById('status').textContent = error.message;
    });
});

refreshAll().catch(error => {
    document.getElementById('status').textContent = error.message;
});

setInterval(() => {
    if (selectedChannelId) {
        loadMessages(true).catch(() => {});
    }
}, 5000);
