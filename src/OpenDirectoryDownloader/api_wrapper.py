from flask import Flask, request, jsonify
import subprocess
import shlex
import logging
import threading
from queue import Queue, Full, Empty

app = Flask(__name__)

# Set up simple logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s')
logger = logging.getLogger()

# Internal queue for managing API requests
request_queue = Queue(maxsize=10)  # Adjust maxsize as needed

def stream_output(process):
    for line in iter(process.stdout.readline, ''):
        logger.info(line.strip())

def process_command(command):
    args = shlex.split(command)
    process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, universal_newlines=True)
    thread = threading.Thread(target=stream_output, args=(process,))
    thread.start()
    try:
        process.wait(timeout=300)
        thread.join()
        return "Command executed successfully", 200
    except subprocess.TimeoutExpired:
        process.kill()
        return "Command timed out", 504
    except Exception as e:
        logger.error(f"An error occurred: {str(e)}")
        return f"An error occurred: {str(e)}", 500

@app.route('/run', methods=['POST'])
def run_command():
    url = request.json.get('url')
    command = f"/app/src/OpenDirectoryDownloader/OpenDirectoryDownloader-linux --postgres --postgres-connection \"Host=postgres_db;Username=postgres;Password=mysecretpassword;Database=postgres\" --url \"{url}\" -q"
    
    try:
        # Add command to the request queue
        request_queue.put_nowait(command)
    except Full:
        logger.warning("Too many requests in the queue")
        return jsonify({"error": "Too many requests in the queue"}), 429
    
    try:
        # Process the command from the queue
        command = request_queue.get_nowait()
        result, status_code = process_command(command)
        return jsonify({"message": result}), status_code
    except Empty:
        logger.error("Failed to get command from the queue")
        return jsonify({"error": "Failed to process the request"}), 500
    finally:
        request_queue.task_done()

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000)
