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
      await loadVoice();
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
      (channel.categoryName || '').toLowerCase().includes(searchTerm)
    );

  if (!guildChannels.length) {
    host.append(el('div', 'empty-copy', searchTerm ? 'No channels matched your search.' : 'No channels available.'));
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

function createProfileLink(userId, displayName, className = 'profile-link') {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = className;
  button.textContent = displayName;
  button.onclick = () => openProfile(userId);
  return button;
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
    return;
  }

  if (!messages.length) {
    const state = el('div', 'empty-state');
    state.append(el('div', 'empty-title', 'No messages loaded yet'));
    state.append(el('div', 'empty-copy', 'Try Refresh, or load a different channel.'));
    host.append(state);
    setMessageMeta();
    setInspector();
    return;
  }

  const displayMessages = [...messages].reverse();

  for (const message of displayMessages) {
    const card = el('article', 'message-card');
    card.append(createAvatar(message));

    const main = el('div', 'message-main');
    const topline = el('div', 'message-topline');
    topline.append(createProfileLink(message.authorId, message.authorName, 'message-author-link'));

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

async function refreshLatestMessages() {
  if (!selectedChannelId || isLoadingMessages || isLoadingOlderMessages) {
    return;
  }

  const container = getMessageScrollHost();
  if (!isNearBottom(container)) {
    return;
  }

  isLoadingMessages = true;
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

    const newScrollHeight = container.scrollHeight;
    const stillNearBottom = isNearBottom(container) || previousScrollTop + container.clientHeight >= previousScrollHeight - 48;
    if (stillNearBottom) {
      scrollMessagesToBottom();
    } else {
      container.scrollTop = previousScrollTop + (newScrollHeight - previousScrollHeight);
    }
  } finally {
    isLoadingMessages = false;
  }
}

function renderVoiceStatusPill(member) {
  const pieces = [];

  if (member.isStreaming) pieces.push('Streaming');
  if (member.isServerMuted || member.isSelfMuted) pieces.push('Muted');
  if (member.isServerDeafened || member.isSelfDeafened) pieces.push('Deafened');
  if (member.isSuppressed) pieces.push('Suppressed');
  if (!pieces.length) pieces.push('Connected');

  return pieces.join(' • ');
}

function createVoiceMemberRow(member) {
  const row = el('div', 'voice-member-row');

  const avatarWrap = el('div', 'voice-avatar-wrap');
  const avatar = member.avatarUrl
    ? (() => {
        const img = document.createElement('img');
        img.className = 'voice-avatar';
        img.src = member.avatarUrl;
        img.alt = member.displayName;
        return img;
      })()
    : el('div', 'voice-avatar-fallback', initials(member.displayName));
  avatarWrap.append(avatar);
  row.append(avatarWrap);

  const main = el('div', 'voice-member-main');
  main.append(createProfileLink(member.userId, member.displayName, 'voice-member-link'));
  main.append(el('div', 'voice-member-status', renderVoiceStatusPill(member)));
  row.append(main);

  const trailing = el('div', 'voice-member-icons');
  if (member.isServerMuted || member.isSelfMuted) trailing.append(el('span', 'voice-icon-pill', 'Mic off'));
  if (member.isServerDeafened || member.isSelfDeafened) trailing.append(el('span', 'voice-icon-pill', 'Deafened'));
  if (member.isStreaming) trailing.append(el('span', 'voice-icon-pill', 'Live'));
  row.append(trailing);

  return row;
}

function renderVoice(snapshot) {
  const summary = document.getElementById('voiceSummary');
  const host = document.getElementById('voiceChannels');

  host.innerHTML = '';

  if (!snapshot || !snapshot.channels || !snapshot.channels.length) {
    summary.textContent = 'No one is connected.';
    host.append(el('div', 'voice-empty', 'Only active voice channels with connected members are shown.'));
    return;
  }

  summary.textContent = `${snapshot.connectedCount} connected across ${snapshot.activeChannelCount} active voice channel${snapshot.activeChannelCount === 1 ? '' : 's'}`;

  for (const channel of snapshot.channels) {
    const card = el('section', 'voice-channel-card');

    const header = el('div', 'voice-channel-header');
    const titleWrap = el('div', 'voice-channel-title-wrap');
    titleWrap.append(el('div', 'voice-channel-name', channel.channelName));
    titleWrap.append(el('div', 'voice-channel-count', `${channel.members.length} connected`));
    header.append(titleWrap);
    card.append(header);

    const memberList = el('div', 'voice-member-list');
    for (const member of channel.members) {
      memberList.append(createVoiceMemberRow(member));
    }
    card.append(memberList);

    host.append(card);
  }
}

async function loadVoice() {
  const btn = document.getElementById('voiceRefreshButton');
  if (btn){ btn.disabled=true; btn.textContent='…'; }
  try{
    const snapshot = await getJson(`/api/voice${selectedGuildId ? `?guildId=${encodeURIComponent(selectedGuildId)}` : ''}`);
    renderVoice(snapshot);
  } finally {
    if (btn){ btn.disabled=false; btn.textContent='↻'; }
  }
}

function createRoleChip(roleName) {
  return el('span', 'profile-role-chip', roleName);
}

function closeProfile() {
  document.getElementById('profileOverlay').classList.add('hidden');
  document.getElementById('profileContent').innerHTML = '';
}

async function openProfile(userId) {
  if (!selectedGuildId || !userId) {
    return;
  }

  const overlay = document.getElementById('profileOverlay');
  const content = document.getElementById('profileContent');

  overlay.classList.remove('hidden');
  content.innerHTML = '<div class="profile-loading">Loading profile…</div>';

  try {
    const profile = await getJson(`/api/profile?guildId=${encodeURIComponent(selectedGuildId)}&userId=${encodeURIComponent(userId)}`);

    const root = el('div', 'profile-card');

    const header = el('div', 'profile-card-header');
    if (profile.avatarUrl) {
      const avatar = document.createElement('img');
      avatar.className = 'profile-avatar';
      avatar.src = profile.avatarUrl;
      avatar.alt = profile.displayName;
      header.append(avatar);
    } else {
      header.append(el('div', 'profile-avatar-fallback', initials(profile.displayName)));
    }

    const identity = el('div', 'profile-identity');
    identity.append(el('div', 'profile-display-name', profile.displayName));
    identity.append(el('div', 'profile-username', `@${profile.username}`));
    if (profile.isBot) {
      identity.append(el('div', 'profile-bot-badge', 'BOT'));
    }
    header.append(identity);
    root.append(header);

    const meta = el('div', 'profile-meta-grid');
    const addRow = (label, value) => {
      const row = el('div', 'profile-meta-row');
      row.append(el('div', 'profile-meta-label', label));
      row.append(el('div', 'profile-meta-value', value || '—'));
      meta.append(row);
    };

    addRow('User ID', profile.userId);
    addRow('Global name', profile.globalName);
    addRow('Nickname', profile.nickname);
    addRow('Created', profile.createdAt);
    addRow('Joined', profile.joinedAt);
    addRow('Voice', profile.isInVoice ? `${profile.voiceChannelName || 'Connected'}${profile.isStreaming ? ' • Streaming' : ''}` : 'Not in voice');
    addRow('Audio state', profile.isMuted || profile.isDeafened
      ? `${profile.isMuted ? 'Muted' : ''}${profile.isMuted && profile.isDeafened ? ' • ' : ''}${profile.isDeafened ? 'Deafened' : ''}`
      : 'Normal');

    root.append(meta);

    const rolesSection = el('div', 'profile-section');
    rolesSection.append(el('div', 'profile-section-title', 'Roles'));
    const rolesWrap = el('div', 'profile-role-list');
    if (profile.roles?.length) {
      for (const role of profile.roles) {
        rolesWrap.append(createRoleChip(role));
      }
    } else {
      rolesWrap.append(el('div', 'profile-muted-copy', 'No roles'));
    }
    rolesSection.append(rolesWrap);
    root.append(rolesSection);

    content.innerHTML = '';
    content.append(root);
  } catch (error) {
    content.innerHTML = '';
    content.append(el('div', 'profile-error', error.message || 'Failed to load profile.'));
  }
}

async function refreshAll() {
  await loadStatus();
  await loadGuilds();
  await loadChannels();
  await loadVoice();

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
    if (selectedChannelId && autoRefreshEnabled) {
      refreshLatestMessages().catch(() => {});
    }

    loadStatus().catch(() => {});
    if (selectedGuildId) {
      loadVoice().catch(() => {});
    }
  }, 5000);
}

document.getElementById('refreshAllButton').addEventListener('click', () => {
  refreshAll().catch(error => setStatus(error.message, true));
});

document.getElementById('refreshButton').addEventListener('click', () => {
  loadMessages(true, { scrollMode: 'bottom' }).catch(error => setStatus(error.message, true));
});

document.getElementById('channelSearch').addEventListener('input', () => {
  renderChannels();
});

document.getElementById('autoRefreshToggle').addEventListener('change', event => {
  autoRefreshEnabled = event.target.checked;
});

document.getElementById('closeProfileButton').addEventListener('click', closeProfile);
document.querySelector('#profileOverlay .profile-backdrop').addEventListener('click', closeProfile);

const messageScrollHost = getMessageScrollHost();
messageScrollHost.addEventListener('scroll', () => {
  if (!selectedChannelId || !hasMoreMessages || !nextBeforeMessageId || isLoadingOlderMessages || isLoadingMessages) {
    return;
  }

  if (messageScrollHost.scrollTop <= 80) {
    loadMessages(false).catch(error => setStatus(error.message, true));
  }
});

document.getElementById('voiceRefreshButton').addEventListener('click', () => {
  loadVoice().catch(error => setStatus(error.message, true));
});

refreshAll()
  .then(startAutoRefresh)
  .catch(error => setStatus(error.message, true));
