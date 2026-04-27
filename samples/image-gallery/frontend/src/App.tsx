import { useEffect, useState, useRef } from 'react'

interface Image {
  id: number
  fileName: string
  contentType: string
  size: number
  thumbnailProcessed: boolean
  uploadedAt: string
}

function App() {
  const [images, setImages] = useState<Image[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadProgress, setUploadProgress] = useState(0)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const dragCounter = useRef(0)
  const [isDragging, setIsDragging] = useState(false)
  const [selectedImage, setSelectedImage] = useState<Image | null>(null)
  const [antiforgeryToken, setAntiforgeryToken] = useState<string | null>(null)

  // Fetch antiforgery token and images on mount
  useEffect(() => {
    fetchAntiforgeryToken()
    fetchImages()
  }, [])

  const fetchAntiforgeryToken = async () => {
    try {
      const response = await fetch('/api/antiforgery')
      const data = await response.json()
      setAntiforgeryToken(data.token)
    } catch (error) {
      console.error('Failed to fetch antiforgery token:', error)
    }
  }

  // Poll for thumbnail updates every 3 seconds
  useEffect(() => {
    const interval = setInterval(() => {
      const hasPendingThumbnails = images.some(img => !img.thumbnailProcessed)
      if (hasPendingThumbnails) {
        fetchImages()
      }
    }, 3000)

    return () => clearInterval(interval)
  }, [images])

  const fetchImages = async () => {
    try {
      const response = await fetch('/api/images')
      const data = await response.json()
      setImages(data)
    } catch (error) {
      console.error('Failed to fetch images:', error)
    }
  }

  const handleFileSelect = async (files: FileList | null) => {
    if (!files || files.length === 0) return

    const file = files[0]
    if (!file.type.startsWith('image/')) {
      alert('Please select an image file')
      return
    }

    if (!antiforgeryToken) {
      alert('Security token not available. Please refresh the page.')
      return
    }

    setUploading(true)
    setUploadProgress(0)

    const formData = new FormData()
    formData.append('file', file)

    try {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
          const percent = (e.loaded / e.total) * 100
          setUploadProgress(Math.round(percent))
        }
      })

      xhr.addEventListener('load', () => {
        if (xhr.status === 201) {
          fetchImages()
          setUploadProgress(0)
          setUploading(false)
          if (fileInputRef.current) {
            fileInputRef.current.value = ''
          }
        } else {
          alert('Upload failed')
          setUploading(false)
        }
      })

      xhr.addEventListener('error', () => {
        alert('Upload failed')
        setUploading(false)
      })

      xhr.open('POST', '/api/images')
      xhr.setRequestHeader('RequestVerificationToken', antiforgeryToken)
      xhr.send(formData)
    } catch (error) {
      console.error('Upload failed:', error)
      alert('Upload failed')
      setUploading(false)
    }
  }

  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current++
    if (e.dataTransfer.items && e.dataTransfer.items.length > 0) {
      setIsDragging(true)
    }
  }

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current--
    if (dragCounter.current === 0) {
      setIsDragging(false)
    }
  }

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    dragCounter.current = 0

    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      handleFileSelect(e.dataTransfer.files)
      e.dataTransfer.clearData()
    }
  }

  const handleDelete = async (id: number) => {
    if (!confirm('Delete this image?')) return

    if (!antiforgeryToken) {
      alert('Security token not available. Please refresh the page.')
      return
    }

    try {
      await fetch(`/api/images/${id}`, {
        method: 'DELETE',
        headers: {
          'RequestVerificationToken': antiforgeryToken
        }
      })
      fetchImages()
    } catch (error) {
      console.error('Delete failed:', error)
      alert('Delete failed')
    }
  }

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return bytes + ' B'
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
  }

  return (
    <div className="app">
      <header>
        <h1>Image Gallery</h1>
        <p>Azure Blob Storage + Container Apps Jobs</p>
      </header>

      <div
        className={`upload-zone ${isDragging ? 'dragging' : ''}`}
        onDragEnter={handleDragEnter}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
      >
        {uploading ? (
          <div className="upload-progress">
            <div className="progress-bar">
              <div className="progress-fill" style={{ width: `${uploadProgress}%` }}></div>
            </div>
            <p>Uploading... {uploadProgress}%</p>
          </div>
        ) : (
          <>
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="17 8 12 3 7 8" />
              <line x1="12" y1="3" x2="12" y2="15" />
            </svg>
            <p>Drop an image here or click to upload</p>
          </>
        )}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          onChange={(e) => handleFileSelect(e.target.files)}
          style={{ display: 'none' }}
        />
      </div>

      <div className="gallery">
        {images.map(image => (
          <div key={image.id} className="image-card">
            <div 
              className="image-container" 
              onClick={() => image.thumbnailProcessed && setSelectedImage(image)}
              style={{ cursor: image.thumbnailProcessed ? 'pointer' : 'default' }}
            >
              {image.thumbnailProcessed ? (
                <img src={`/api/images/${image.id}/thumbnail`} alt={image.fileName} />
              ) : (
                <div className="processing">
                  <div className="spinner"></div>
                  <p>Processing thumbnail...</p>
                </div>
              )}
            </div>
            <div className="image-info">
              <p className="filename" title={image.fileName}>{image.fileName}</p>
              <p className="filesize">{formatFileSize(image.size)}</p>
              <button onClick={() => handleDelete(image.id)} className="delete-btn">
                Delete
              </button>
            </div>
          </div>
        ))}
      </div>

      {images.length === 0 && !uploading && (
        <div className="empty-state">
          <p>No images yet. Upload one to get started!</p>
        </div>
      )}

      {selectedImage && (
        <div className="modal-overlay" onClick={() => setSelectedImage(null)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <button className="modal-close" onClick={() => setSelectedImage(null)}>×</button>
            <img src={`/api/images/${selectedImage.id}/blob`} alt={selectedImage.fileName} />
            <div className="modal-info">
              <p>{selectedImage.fileName}</p>
              <p>{formatFileSize(selectedImage.size)}</p>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default App
