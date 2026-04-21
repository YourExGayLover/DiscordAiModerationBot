let selectedGuildId = null;
let selectedChannelId = null;

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
            await loadGuilds();
            await loadChannels();
            renderMessages([]);
            document.getElementById('currentChannel').textContent = 'Select a channel';
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
    const channels = await getJson(`/api/channels${selectedGuildId ? `?guildId=${selectedGuildId}` : ''}`);
    const host = document.getElementById('channels');
    host.innerHTML = '';

    for (const channel of channels) {
        const button = el('button', 'list-item' + (channel.id === selectedChannelId ? ' active' : ''));
        button.append(el('div', null, `# ${channel.name}`));
        button.append(el('span', 'secondary', `${channel.recentMessageCount} cached messages`));
        button.onclick = async () => {
            selectedChannelId = channel.id;
            document.getElementById('currentChannel').textContent = `# ${channel.name}`;
            await loadChannels();
            await loadMessages();
        };
        host.append(button);
    }
}

function renderMessages(messages) {
    const host = document.getElementById('messages');
    host.innerHTML = '';

    if (!messages.length) {
        host.append(el('div', 'status', 'No cached messages yet for this channel.'));
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

async function loadMessages() {
    if (!selectedChannelId) {
        renderMessages([]);
        return;
    }

    const messages = await getJson(`/api/channels/${selectedChannelId}/messages`);
    renderMessages(messages);
}

async function refreshAll() {
    await loadStatus();
    await loadGuilds();
    if (selectedChannelId) {
        await loadMessages();
    }
}

document.getElementById('refreshButton').addEventListener('click', refreshAll);
refreshAll().catch(error => {
    document.getElementById('status').textContent = error.message;
});
setInterval(() => {
    if (selectedChannelId) {
        loadMessages().catch(() => {});
    }
}, 5000);
